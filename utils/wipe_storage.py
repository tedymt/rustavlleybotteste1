"""Armazenamento de configuração de wipe (datas, servidores RCON, timezone)."""
from pathlib import Path
import json
import os
import uuid

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


# Opções padrão do embed por countdown (sala)
DEFAULT_EMBED_OPTIONS = {
    "show_server_name": True,
    "show_player_count": True,
    "show_player_list": True,
    "show_wipe_countdown": True,
    "show_banner": True,
}


def get_all_wipe_guild_ids() -> list[str]:
    """Retorna todos os guild_ids que têm config de wipe (para restaurar tasks ao iniciar o bot)."""
    return list(_load().keys())


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
        "countdowns": [],
        # Anúncio de wipe: config separada por idioma (PT-BR e US)
        "wipe_announce_pt": None,  # { channel_id, map_link, store_url, aviso, rcon_index }
        "wipe_announce_us": None,
    }
    if guild_id not in data:
        data[guild_id] = defaults.copy()
        _save(data)
    cfg = data[guild_id]
    for k, v in defaults.items():
        if k not in cfg:
            cfg[k] = v
            _save(data)
    # Migrar config antiga de anúncio para wipe_announce_pt / wipe_announce_us
    if cfg.get("wipe_announce_pt") is None and (cfg.get("wipe_announce_channel_pt") or cfg.get("wipe_announce_channel_id")):
        cfg["wipe_announce_pt"] = {
            "channel_id": cfg.get("wipe_announce_channel_pt") or cfg.get("wipe_announce_channel_id"),
            "map_link": cfg.get("wipe_announce_map_link"),
            "store_url": cfg.get("wipe_announce_store_url"),
            "aviso": cfg.get("wipe_announce_aviso"),
            "rcon_index": cfg.get("wipe_announce_rcon_index", 0),
        }
        _save(data)
    if cfg.get("wipe_announce_us") is None and cfg.get("wipe_announce_channel_us"):
        cfg["wipe_announce_us"] = {
            "channel_id": cfg.get("wipe_announce_channel_us"),
            "map_link": cfg.get("wipe_announce_map_link"),
            "store_url": cfg.get("wipe_announce_store_url"),
            "aviso": cfg.get("wipe_announce_aviso"),
            "rcon_index": cfg.get("wipe_announce_rcon_index", 0),
        }
        _save(data)
    # Garantir que são dicts
    for key in ("wipe_announce_pt", "wipe_announce_us"):
        if cfg.get(key) is None:
            cfg[key] = {"channel_id": None, "map_link": None, "store_url": None, "aviso": None, "rcon_index": 0}
        elif isinstance(cfg[key], dict):
            for f in ("channel_id", "map_link", "store_url", "aviso", "rcon_index"):
                if f not in cfg[key]:
                    cfg[key][f] = None if f != "rcon_index" else 0
            if cfg[key].get("rcon_index") is None:
                cfg[key]["rcon_index"] = 0
    return cfg


def save_wipe_config(guild_id: str, config: dict) -> None:
    """Salva config de wipe."""
    data = _load()
    data[guild_id] = {**get_wipe_config(guild_id), **config}
    _save(data)


def _ensure_embed_options(cd: dict) -> dict:
    """Garante que embed_options tem todas as chaves padrão."""
    opts = cd.get("embed_options") or {}
    for k, v in DEFAULT_EMBED_OPTIONS.items():
        if k not in opts:
            opts[k] = v
    cd["embed_options"] = opts
    return cd


def _ensure_countdown_language(cd: dict) -> dict:
    """Garante que o countdown tenha idioma válido (pt/en)."""
    lang = (cd.get("lang") or "pt").strip().lower()
    if lang not in ("pt", "en"):
        lang = "pt"
    cd["lang"] = lang
    return cd


def add_countdown(
    guild_id: str,
    channel_id: int,
    rcon_index: int,
    wipe_datetime_utc: str,
    banner_url: str | None = None,
    label: str | None = None,
    lang: str = "pt",
) -> str:
    """Adiciona um countdown por sala. Retorna o id do countdown."""
    cfg = get_wipe_config(guild_id)
    countdowns = list(cfg.get("countdowns") or [])
    cd_id = str(uuid.uuid4())[:8]
    entry = {
        "id": cd_id,
        "label": label or f"Sala {len(countdowns) + 1}",
        "channel_id": channel_id,
        "rcon_index": rcon_index,
        "wipe_datetime_utc": wipe_datetime_utc,
        "banner_url": banner_url or None,
        "message_id": None,
        "embed_options": dict(DEFAULT_EMBED_OPTIONS),
        "lang": (lang or "pt").strip().lower() if (lang or "").strip().lower() in ("pt", "en") else "pt",
    }
    countdowns.append(entry)
    cfg["countdowns"] = countdowns
    save_wipe_config(guild_id, cfg)
    return cd_id


def remove_countdown(guild_id: str, cd_id: str) -> bool:
    """Remove um countdown. Retorna True se existia."""
    cfg = get_wipe_config(guild_id)
    countdowns = [c for c in (cfg.get("countdowns") or []) if c.get("id") != cd_id]
    if len(countdowns) == len(cfg.get("countdowns") or []):
        return False
    cfg["countdowns"] = countdowns
    save_wipe_config(guild_id, cfg)
    return True


def get_countdown(guild_id: str, cd_id: str) -> dict | None:
    """Retorna um countdown pelo id."""
    for c in get_wipe_config(guild_id).get("countdowns") or []:
        if c.get("id") == cd_id:
            return _ensure_countdown_language(_ensure_embed_options(dict(c)))
    return None


def list_countdowns(guild_id: str) -> list[dict]:
    """Lista todos os countdowns da guilda (com embed_options preenchidas)."""
    countdowns = get_wipe_config(guild_id).get("countdowns") or []
    return [_ensure_countdown_language(_ensure_embed_options(dict(c))) for c in countdowns]


def set_countdown_message_id(guild_id: str, cd_id: str, message_id: int | None) -> None:
    """Define message_id do countdown (quando inicia/para)."""
    cfg = get_wipe_config(guild_id)
    countdowns = list(cfg.get("countdowns") or [])
    for i, c in enumerate(countdowns):
        if c.get("id") == cd_id:
            countdowns[i] = {**c, "message_id": message_id}
            break
    cfg["countdowns"] = countdowns
    save_wipe_config(guild_id, cfg)


def set_countdown_embed_options(guild_id: str, cd_id: str, options: dict) -> None:
    """Atualiza opções do embed de um countdown."""
    cfg = get_wipe_config(guild_id)
    countdowns = list(cfg.get("countdowns") or [])
    for i, c in enumerate(countdowns):
        if c.get("id") == cd_id:
            opts = dict(c.get("embed_options") or DEFAULT_EMBED_OPTIONS)
            for k, v in options.items():
                if k in DEFAULT_EMBED_OPTIONS:
                    opts[k] = bool(v)
            countdowns[i] = {**c, "embed_options": opts}
            break
    cfg["countdowns"] = countdowns
    save_wipe_config(guild_id, cfg)
