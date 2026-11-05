# -*- coding: utf-8 -*-
"""Per-button settings and files, like pyRevit's script module.

get_config()       the settings of the running button: attributes + get_option(name, default)
save_config()      write them (%APPDATA%\\claude-using-revit\\tool-config\\<button>.json)
read_config(name)  the settings of another button as a dict (tasks.py reads what the buttons remembered)
get_bundle_file(f) a file next to the button's script.py (ui.xaml, icon.png), None when missing
exit()             stop the script quietly
A button without its own settings file yet starts from what pyRevit had remembered for it, if anything.
"""
import io
import json
import os
import sys


def _ctx():
    try:
        return __cur__  # noqa: F821 (set by the add-in)
    except NameError:
        return None


def _folder():
    c = _ctx()
    d = c.ToolConfigDir if c is not None else os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'tool-config')
    if not os.path.isdir(d):
        os.makedirs(d)
    return d


def _bundle_name():
    c = _ctx()
    return (c.BundleName if c is not None else None) or u'startup'


def _path(name):
    safe = u''.join(ch if ch.isalnum() or ch in u' ._-' else u'_' for ch in name)
    return os.path.join(_folder(), safe + u'.json')


def _from_pyrevit(name):
    """Values pyRevit kept for this button ([<button>_config] of pyRevit_config.ini), once."""
    out = {}
    ini = os.path.join(os.environ.get('APPDATA', ''), 'pyRevit', 'pyRevit_config.ini')
    if not os.path.isfile(ini):
        return out
    try:
        section = None
        with io.open(ini, encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if line.startswith(u'[') and line.endswith(u']'):
                    section = line[1:-1]
                elif section == name + u'_config' and u'=' in line:
                    key, val = [x.strip() for x in line.split(u'=', 1)]
                    try:
                        out[key] = json.loads(val)
                    except ValueError:
                        out[key] = val
    except Exception:
        pass
    return out


def read_config(name):
    p = _path(name)
    if os.path.isfile(p):
        try:
            with io.open(p, encoding='utf-8') as f:
                return json.load(f)
        except Exception:
            return {}
    return _from_pyrevit(name)


class Config(object):
    def __init__(self, name):
        object.__setattr__(self, '_name', name)
        object.__setattr__(self, '_values', read_config(name))

    def __getattr__(self, key):
        values = object.__getattribute__(self, '_values')
        if key in values:
            return values[key]
        raise AttributeError(key)

    def __setattr__(self, key, value):
        self._values[key] = value

    def get_option(self, key, default=None):
        return self._values.get(key, default)

    def set_option(self, key, value):
        self._values[key] = value

    def has_option(self, key):
        return key in self._values

    def save(self):
        p = _path(self._name)
        text = json.dumps(self._values, ensure_ascii=False, indent=1)
        with io.open(p + u'.tmp', 'w', encoding='utf-8') as f:
            f.write(text if isinstance(text, unicode) else text.decode('utf-8'))  # noqa: F821 (IronPython 2)
        if os.path.exists(p):
            os.remove(p)
        os.rename(p + u'.tmp', p)


_configs = {}


def get_config(section=None):
    name = section or _bundle_name()
    if name not in _configs:
        _configs[name] = Config(name)
    return _configs[name]


def save_config():
    for c in _configs.values():
        c.save()


def get_bundle_file(file_name):
    c = _ctx()
    d = c.BundleDir if c is not None else None
    if not d:
        return None
    p = os.path.join(d, file_name)
    return p if os.path.exists(p) else None


def exit():
    sys.exit()
