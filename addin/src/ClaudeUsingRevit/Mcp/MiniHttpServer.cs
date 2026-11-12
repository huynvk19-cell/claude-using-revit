using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ClaudeUsingRevit.Core;

namespace ClaudeUsingRevit.Mcp
{
    public class HttpRequest
    {
        public string Method;
        public string Path;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = new byte[0];
        public string Remote;

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
        public string BodyText => Encoding.UTF8.GetString(Body);
    }

    /// <summary>
    /// A small HTTP/1.1 server on a TcpListener (no HTTP.sys, so no URL ACL and no admin rights), bound to
    /// 127.0.0.1 with an exclusive port: the first Revit window gets it, the others see "in use".
    /// Enough for MCP Streamable HTTP: keep-alive, Content-Length or chunked bodies, server-sent events.
    /// </summary>
    public class MiniHttpServer
    {
        const int MaxHeader = 64 * 1024;
        const int MaxBody = 32 * 1024 * 1024;

        readonly Action<HttpRequest, HttpConnection> _handler;
        readonly ConcurrentDictionary<HttpConnection, byte> _open = new ConcurrentDictionary<HttpConnection, byte>();
        TcpListener _listener;
        volatile bool _stopping;

        public int Port { get; private set; }

        const uint HANDLE_FLAG_INHERIT = 1;
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        public MiniHttpServer(Action<HttpRequest, HttpConnection> handler) { _handler = handler; }

        public bool Start(int port, out string error)
        {
            error = null;
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                try { l.ExclusiveAddressUse = true; } catch { /* not on every OS; Windows has it */ }
                l.Start();
                // .NET Framework makes the socket inheritable: a program this Revit starts later (the web app's node)
                // would keep the port open after Revit lets go of it, and no other window could take it.
                try { SetHandleInformation(l.Server.Handle, HANDLE_FLAG_INHERIT, 0); } catch { }
                _listener = l;
                Port = port;
            }
            catch (SocketException ex)
            {
                error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse ? "in use" : ex.Message;
                return false;
            }
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "ClaudeUsingRevit MCP accept" };
            t.Start();
            return true;
        }

        public void Stop()
        {
            _stopping = true;
            try { _listener?.Stop(); } catch { }
            foreach (var c in _open.Keys) c.Close();
        }

        void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_stopping) return; continue; }
                var conn = new HttpConnection(client);
                _open[conn] = 0;
                var t = new Thread(() => Serve(conn)) { IsBackground = true, Name = "ClaudeUsingRevit MCP conn" };
                t.Start();
            }
        }

        void Serve(HttpConnection conn)
        {
            try
            {
                while (!_stopping && !conn.Closed)
                {
                    var req = conn.ReadRequest(MaxHeader, MaxBody);
                    if (req == null) break;
                    _handler(req, conn);
                    if (conn.Streaming || conn.CloseAfterResponse) break;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Error("http", ex); }
            finally
            {
                conn.Close();
                _open.TryRemove(conn, out _);
            }
        }
    }

    public class HttpConnection
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly byte[] _buf = new byte[16 * 1024];
        int _start, _end;                 // unread bytes in _buf
        readonly object _writeGate = new object();

        public bool Closed { get; private set; }
        public bool Streaming { get; private set; }
        public bool CloseAfterResponse { get; set; }
        public string Remote { get; }

        public HttpConnection(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            Remote = client.Client.RemoteEndPoint?.ToString();
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            try { _client.Close(); } catch { }
        }

        // ---- reading ----------------------------------------------------------------------

        int ReadByte()
        {
            if (_start == _end)
            {
                _start = 0;
                _end = _stream.Read(_buf, 0, _buf.Length);
                if (_end <= 0) { _end = 0; return -1; }
            }
            return _buf[_start++];
        }

        string ReadLine(int max)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
                if (sb.Length > max) throw new IOException("header too long");
            }
            return sb.ToString();
        }

        void ReadExact(byte[] dst, int offset, int count)
        {
            while (count > 0)
            {
                if (_start < _end)
                {
                    int n = Math.Min(count, _end - _start);
                    Buffer.BlockCopy(_buf, _start, dst, offset, n);
                    _start += n; offset += n; count -= n;
                    continue;
                }
                int r = _stream.Read(dst, offset, count);
                if (r <= 0) throw new IOException("connection closed in body");
                offset += r; count -= r;
            }
        }

        public HttpRequest ReadRequest(int maxHeader, int maxBody)
        {
            string first = ReadLine(maxHeader);
            while (first != null && first.Length == 0) first = ReadLine(maxHeader);   // stray CRLF between requests
            if (first == null) return null;
            var parts = first.Split(' ');
            if (parts.Length < 2) throw new IOException("bad request line");
            var req = new HttpRequest { Method = parts[0].ToUpperInvariant(), Path = parts[1], Remote = Remote };
            int total = 0;
            while (true)
            {
                string line = ReadLine(maxHeader);
                if (line == null) return null;
                if (line.Length == 0) break;
                total += line.Length;
                if (total > maxHeader) throw new IOException("headers too long");
                int c = line.IndexOf(':');
                if (c > 0) req.Headers[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
            }
            if (string.Equals(req.Header("Connection"), "close", StringComparison.OrdinalIgnoreCase)) CloseAfterResponse = true;
            if ((req.Header("Transfer-Encoding") ?? "").IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var ms = new MemoryStream();
                while (true)
                {
                    string size = ReadLine(1024);
                    if (size == null) throw new IOException("connection closed in chunk");
                    int semi = size.IndexOf(';');
                    int n = Convert.ToInt32((semi >= 0 ? size.Substring(0, semi) : size).Trim(), 16);
                    if (n == 0) { while (!string.IsNullOrEmpty(ReadLine(maxHeader))) { } break; }
                    if (ms.Length + n > maxBody) throw new IOException("body too large");
                    var chunk = new byte[n];
                    ReadExact(chunk, 0, n);
                    ms.Write(chunk, 0, n);
                    ReadLine(16);
                }
                req.Body = ms.ToArray();
            }
            else if (int.TryParse(req.Header("Content-Length"), out int len) && len > 0)
            {
                if (len > maxBody) throw new IOException("body too large");
                req.Body = new byte[len];
                ReadExact(req.Body, 0, len);
            }
            return req;
        }

        // ---- writing ----------------------------------------------------------------------

        static string Reason(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 413: return "Payload Too Large";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
        }

        public void Send(int code, string contentType, string body, IDictionary<string, string> headers = null)
        {
            var data = Encoding.UTF8.GetBytes(body ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n");
            if (data.Length > 0 || code != 202) sb.Append("Content-Type: ").Append(contentType ?? "application/json; charset=utf-8").Append("\r\n");
            sb.Append("Content-Length: ").Append(data.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            if (headers != null) foreach (var h in headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append(CloseAfterResponse ? "Connection: close\r\n" : "Connection: keep-alive\r\n");
            sb.Append("\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            lock (_writeGate)
            {
                _stream.Write(head, 0, head.Length);
                if (data.Length > 0) _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }
        }

        public void SendJson(int code, string json) => Send(code, "application/json; charset=utf-8", json);

        /// <summary>Start a server-sent-events stream; then WriteEvent / WriteComment until it fails.</summary>
        public void StartEventStream()
        {
            Streaming = true;
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n" +
                                               "Cache-Control: no-store\r\nConnection: keep-alive\r\n\r\n");
            lock (_writeGate) { _stream.Write(head, 0, head.Length); _stream.Flush(); }
        }

        public void WriteEvent(string data)
        {
            var bytes = Encoding.UTF8.GetBytes("event: message\ndata: " + data.Replace("\n", "\ndata: ") + "\n\n");
            lock (_writeGate) { _stream.Write(bytes, 0, bytes.Length); _stream.Flush(); }
        }

        public void WriteComment(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(": " + text + "\n\n");
            lock (_writeGate) { _stream.Write(bytes, 0, bytes.Length); _stream.Flush(); }
        }
    }
}
