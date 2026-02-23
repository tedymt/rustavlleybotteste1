"""Armazenamento de configuração de wipe (datas, servidores RCON, timezone)."""
from pathlib import Path
import json
import os

_BASE_DIR = Path(os.getenv("DISCORD_DATA_DIR", "") or Path(__file__).resolve().parent.parent)
_WIPE_PATH = _BASE_DIR / "data" / "wipe.json"


def _load() -> dict:
    if _WIPE_PATH.exists():
        with open(_WIPE_PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    return {}


def _save(data: dict) -> None:
    _WIPE_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(_WIPE_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def get_wipe_config(guild_id: str) -> dict:
    """Retorna config de wipe da guilda."""
    data = _load()
    defaults = {
        "wipe_datetime_br": None,
        "wipe_datetime_utc": None,
        "countdown_channel_id": None,
        "countdown_message_id": None,
        "wipe_embed_channel_id": None,
        "wipe_embed_message_id": None,
        "banner_url": None,
        "rcon_servers": [],
        "rcon_draft": {},
    }
    if guild_id not in data:
        data[guild_id] = defaults.copy()
        _save(data)
    cfg = data[guild_id]
    for k, v in defaults.items():
        if k not in cfg:
            cfg[k] = v
            _save(data)
    return cfg


def save_wipe_config(guild_id: str, config: dict) -> None:
    """Salva config de wipe."""
    data = _load()
    data[guild_id] = {**get_wipe_config(guild_id), **config}
    _save(data)
