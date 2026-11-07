# -*- coding: utf-8 -*-
"""Keep the installed skills, workflows, step cards, standards and MCP commands in step with GitHub, without Claude.

startup.py calls start(): at most once per CHECK_HOURS over all Revit windows, a background thread compares the newest
commit on GitHub with the one last installed, downloads the repo's zip when they differ and copies the content files over
(what install.ps1 copies without -AddIn, and revit-commands/). Nothing happens while run_state says a job is running: it
is tried again on the next start. A skill's folder is mirrored, so a removed card goes too. The add-in's own code
(addin/) is never replaced here, Revit locks it: a change there only leaves a note, shown by the Workflow window.
Every failure is quiet (no network, rate limit) and is tried again later. config.json: "autoUpdate": false turns it off, and a
Dev install (extensionDir set) never updates.
"""
import io
import json
import os
import shutil
import threading
import time

import clr
clr.AddReference('System')

from System import DateTime
from System.IO import Path
from System.Net import HttpWebRequest, SecurityProtocolType, ServicePointManager

import workflow_lib as lib

REPO = u'huynvk19-cell/claude-using-revit'
BRANCH = u'main'
CHECK_HOURS = 6
NOTE_HOURS = 24                 # how long the window says what was updated
TIMEOUT_MS = 20000
STATE = os.path.join(lib.DATA_DIR, u'content-update.json')
LOCK = os.path.join(lib.DATA_DIR, u'content-update.lock')
CLAUDE = os.path.join(lib.HOME, u'.claude')
LOCK_STALE_S = 600


def _state():
    try:
        return json.loads(lib.read(STATE))
    except Exception:
        return {}


def _save(state):
    with io.open(STATE, 'w', encoding='utf-8') as f:
        f.write(u'' + json.dumps(state, indent=1))


def _config():
    try:
        return json.loads(lib.read(os.path.join(lib.DATA_DIR, u'config.json')))
    except Exception:
        return {}


def note():
    """One line for the Workflow window."""
    st = _state()
    parts = []
    if st.get('waiting'):
        parts.append(u'Có bản mới trên GitHub, sẽ cập nhật khi không có việc đang chạy.')
    elif st.get('changed') and time.time() - st.get('when', 0) < NOTE_HOURS * 3600:
        parts.append(u'Đã cập nhật {0} card lúc {1}.'.format(st['changed'], st.get('at', u'')))
    if st.get('addinSince') and _installed_time() < st['addinSince']:
        parts.append(u'Add-in có bản mới, cần cài lại khi đóng Revit.')
    return u' '.join(parts)


def _installed_time():
    """When the add-in itself was last installed."""
    newest = 0
    folder = os.path.join(lib.DATA_DIR, u'bin')
    if os.path.isdir(folder):
        for version in os.listdir(folder):
            path = os.path.join(folder, version, u'ClaudeUsingRevit.dll')
            if os.path.isfile(path):
                newest = max(newest, os.path.getmtime(path))
    return newest


def _get(url, accept=None):
    req = HttpWebRequest.Create(url)
    req.UserAgent = u'claude-using-revit'
    req.Timeout = TIMEOUT_MS
    req.ReadWriteTimeout = TIMEOUT_MS
    if accept:
        req.Accept = accept
    return req.GetResponse()


def _text(url):
    from System.IO import StreamReader
    resp = _get(url, u'application/vnd.github+json')
    try:
        return StreamReader(resp.GetResponseStream()).ReadToEnd()
    finally:
        resp.Close()


def _download(url, path):
    from System.IO import FileMode, FileStream
    resp = _get(url)
    try:
        out = FileStream(path, FileMode.Create)
        try:
            resp.GetResponseStream().CopyTo(out)
        finally:
            out.Close()
    finally:
        resp.Close()


def _unzip(zip_path, folder):
    try:
        clr.AddReference('System.IO.Compression.FileSystem')
    except Exception:
        clr.AddReference('System.IO.Compression.ZipFile')
    from System.IO.Compression import ZipFile
    ZipFile.ExtractToDirectory(zip_path, folder)


def _copy_tree(src, dst, mirror=False):
    """mirror: also remove what src no longer has, after the copying, so a failure leaves nothing missing."""
    if not os.path.isdir(dst):
        os.makedirs(dst)
    names = os.listdir(src)
    for name in names:
        s, d = os.path.join(src, name), os.path.join(dst, name)
        if os.path.isdir(s):
            _copy_tree(s, d, mirror)
        else:
            shutil.copyfile(s, d)
    if mirror:
        for name in os.listdir(dst):
            if name not in names:
                d = os.path.join(dst, name)
                if os.path.isdir(d):
                    shutil.rmtree(d)
                else:
                    os.remove(d)


def _cards(folder):
    """{skill/steps/file: contents} of the step cards."""
    out = {}
    if not os.path.isdir(folder):
        return out
    for skill in os.listdir(folder):
        steps = os.path.join(folder, skill, u'steps')
        if os.path.isdir(steps):
            for f in os.listdir(steps):
                with io.open(os.path.join(steps, f), 'rb') as h:
                    out[skill + u'/' + f] = h.read()
    return out


def install(root):
    """Copy the content of an unpacked repo at root. Returns how many step cards changed."""
    skills_to = os.path.join(CLAUDE, u'skills')
    before = _cards(skills_to)
    skills_from = os.path.join(root, u'skills')
    for name in os.listdir(skills_from):
        _copy_tree(os.path.join(skills_from, name), os.path.join(skills_to, name), mirror=True)
    domain = os.path.join(CLAUDE, u'drafting-domain')
    _copy_tree(os.path.join(root, u'domain'), domain)
    for sub in (u'templates', u'tools'):
        if os.path.isdir(os.path.join(root, sub)):
            _copy_tree(os.path.join(root, sub), os.path.join(domain, sub))
    _copy_tree(os.path.join(root, u'workflows'), os.path.join(domain, u'workflows'), mirror=True)
    if os.path.isdir(os.path.join(root, u'agents')):
        _copy_tree(os.path.join(root, u'agents'), os.path.join(CLAUDE, u'agents'))
    commands = os.path.join(lib.DATA_DIR, u'commands')
    if os.path.isdir(commands):         # the skills name these commands; they compile on the next call
        _copy_tree(os.path.join(root, u'revit-commands'), commands)
    after = _cards(skills_to)
    return len([k for k in set(before) | set(after) if before.get(k) != after.get(k)])


def _addin_changed(old, new):
    if not old:
        return False
    data = json.loads(_text(u'https://api.github.com/repos/{0}/compare/{1}...{2}'.format(REPO, old, new)))
    return any(f.get('filename', u'').startswith(u'addin/') for f in data.get('files', []))


def _check():
    state = _state()
    data = json.loads(_text(u'https://api.github.com/repos/{0}/commits/{1}'.format(REPO, BRANCH)))
    sha = data['sha']
    state['checked'] = time.time()
    if sha == state.get('sha'):
        state.pop('waiting', None)
        _save(state)
        return
    if lib.run_status() is not None:      # not under a running job: tried again on the next start
        state['waiting'] = sha
        state['checked'] = 0
        _save(state)
        return
    temp = Path.Combine(Path.GetTempPath(), u'claude-using-revit-update-' + sha[:12])
    try:
        if os.path.isdir(temp):
            shutil.rmtree(temp)
        os.makedirs(temp)
        zip_path = os.path.join(temp, u'repo.zip')
        _download(u'https://codeload.github.com/{0}/zip/{1}'.format(REPO, sha), zip_path)
        out = os.path.join(temp, u'repo')
        _unzip(zip_path, out)
        root = os.path.join(out, os.listdir(out)[0])
        changed = install(root)
    finally:
        shutil.rmtree(temp, ignore_errors=True)
    if _addin_changed(state.get('sha'), sha):
        state['addinSince'] = time.time()
    state.update({'sha': sha, 'changed': changed, 'when': time.time(), 'at': DateTime.Now.ToString(u'dd/MM HH:mm')})
    state.pop('waiting', None)
    _save(state)


def _take_lock():
    try:
        if os.path.isfile(LOCK) and time.time() - os.path.getmtime(LOCK) > LOCK_STALE_S:
            os.remove(LOCK)
        fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.close(fd)
        return True
    except Exception:
        return False


def _run():
    if not _take_lock():
        return
    try:
        _check()
    except Exception as ex:
        try:
            from cur import get_logger
            get_logger('claude-using-revit').info('content update: %s', ex)
        except Exception:
            pass
    finally:
        try:
            os.remove(LOCK)
        except Exception:
            pass


def start():
    """Quick: the work is on a background thread."""
    cfg = _config()
    if cfg.get('autoUpdate') is False or cfg.get('extensionDir') or cfg.get('commandsDir'):
        return False
    if time.time() - _state().get('checked', 0) < CHECK_HOURS * 3600:
        return False
    ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12
    t = threading.Thread(target=_run)
    t.daemon = True
    t.start()
    return True
