using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiCuli.Core
{
    public static class Json
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.None,
        };

        public static string Serialize(object value)
        {
            if (value == null) return "null";
            if (value is string s) return s;
            if (value is JToken t) return t.ToString(Formatting.None);
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static string Error(string message, string trace = null)
        {
            var o = new JObject { ["error"] = message };
            if (!string.IsNullOrEmpty(trace)) o["trace"] = trace;
            return o.ToString(Formatting.None);
        }
    }
}
