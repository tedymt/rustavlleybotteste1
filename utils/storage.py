"""Gerenciamento de dados em JSON - sem banco de dados."""
import json
import os
from pathlib import Path
from typing import Any

from utils.limits import trim_config

# Permite usar volume persistente no Discloud (env DISCORD_DATA_DIR=/data)
_BASE_DIR = Path(os.getenv("DISCORD_DATA_DIR", "") or os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def _data_path(*parts: str) -> Path:
    """Retorna path em data/ (ou DISCORD_DATA_DIR)."""
    return _BASE_DIR / "data" / Path(*parts)


def _transcripts_path() -> Path:
    return _BASE_DIR / "transcripts"


def _load_json(path: Path, default: Any = None) -> dict | list:
    """Carrega arquivo JSON. Retorna default se não existir."""
    if path.exists():
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    return default if default is not None else {}


def _save_json(path: Path, data: dict | list) -> None:
    """Salva dados em JSON com formatação legível."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def get_guild_config(guild_id: str) -> dict:
    """Retorna config da guilda. Cria estrutura padrão se não existir."""
    path = _data_path("guilds.json")
    data = _load_json(path, {})
    defaults = {
        "category_id": None,
        "category_id_pt": None,
        "category_id_en": None,
        "logs_channel_id": None,
        "transcript_channel_id": None,
        "support_role_id": None,
        "title": "Central de Suporte / Support Center",
        "description": "🇧🇷 Clique abaixo para abrir um ticket.\n🇺🇸 Click below to open a ticket.",
        "color": "#5865F2",
        "transcript_to_dm": False,
        "departments": [],
        "allowed_sup_users": [],
        "servers": [],  # [{"id":"1","name":"EU1"}] — lista para seleção, categoria vem de PT/EN
        "panel_title_pt": "Central de Suporte",
        "panel_title_en": "Support Center",
        "panel_desc_pt": "🇧🇷 Clique no botão abaixo para abrir um ticket.\nNossa equipe irá atendê-lo em breve.",
        "panel_desc_en": "🇺🇸 Click the button below to open a ticket.\nOur team will assist you shortly.",
        "panel_banner": None,
        "ticket_title_pt": "Atendimento Iniciado",
        "ticket_title_en": "Support Started",
        "ticket_desc_pt": "Olá! Nossa equipe foi notificada e irá atendê-lo em breve.",
        "ticket_desc_en": "Hello! Our team has been notified and will assist you shortly.",
        "ticket_banner": None,
        "agent_enabled": False,
        "agent_channels": [],
        "agent_categories": [],
        "agent_teachings": [],
        "agent_in_tickets": False,
        "agent_log_channel": None,
        "agent_training_channel": None,
        "agent_learned_data": [],
        "ticket_translation_enabled": False,
        "agent_instructions": "",
        "agent_ai_enabled": True,
        "agent_ticket_channel": None,
        "bot_log_channel_id": None,
        "doubt_use_ai_validation": True,
        # Tempos de ticket (para testes use valores baixos: ex. 5, 2, 1, 5 em minutos/segundos)
        "ticket_close_delay_seconds": 5,
        "ticket_unanswered_alert_minutes": 240,
        "ticket_staff_reminder_minutes": 60,
        "ticket_author_inactivity_close_minutes": 480,
    }
    if guild_id not in data:
        data[guild_id] = defaults.copy()
        _save_json(path, data)
    cfg = data[guild_id]
    for k, v in defaults.items():
        if k not in cfg:
            cfg[k] = v
            _save_json(path, data)
    # IA sempre ativa quando há API configurada
    cfg["agent_ai_enabled"] = True
    return trim_config(data[guild_id])


def save_guild_config(guild_id: str, config: dict) -> None:
    """Salva config da guilda (aplica limites antes de salvar)."""
    path = _data_path("guilds.json")
    data = _load_json(path, {})
    data[guild_id] = trim_config(dict(config))
    _save_json(path, data)


def get_all_tickets() -> dict:
    """Retorna todos os tickets: {guild_id: [tickets]}."""
    path = _data_path("tickets.json")
    return _load_json(path, {})


def get_open_tickets(guild_id: str) -> list[dict]:
    """Retorna tickets abertos da guilda."""
    data = get_all_tickets()
    guild_tickets = data.get(guild_id, [])
    return [t for t in guild_tickets if t.get("status") == "OPEN"]


def get_ticket_by_channel(channel_id: str) -> dict | None:
    """Busca ticket pelo ID do canal."""
    data = get_all_tickets()
    for guild_id, tickets in data.items():
        for t in tickets:
            if str(t.get("channel_id")) == str(channel_id):
                return t
    return None


def get_ticket_by_code(code: str) -> dict | None:
    """Busca ticket pelo código."""
    data = get_all_tickets()
    for guild_id, tickets in data.items():
        for t in tickets:
            if t.get("ticket_code") == code:
                return t
    return None


def add_ticket(guild_id: str, ticket: dict) -> None:
    """Adiciona ticket e salva."""
    path = _data_path("tickets.json")
    data = _load_json(path, {})
    if guild_id not in data:
        data[guild_id] = []
    data[guild_id].append(ticket)
    _save_json(path, data)


def update_ticket(channel_id: str, updates: dict) -> None:
    """Atualiza ticket pelo channel_id."""
    path = _data_path("tickets.json")
    data = _load_json(path, {})
    for guild_id, tickets in data.items():
        for i, t in enumerate(tickets):
            if str(t.get("channel_id")) == str(channel_id):
                data[guild_id][i].update(updates)
                _save_json(path, data)
                return


def save_transcript(ticket: dict, messages: list[dict]) -> str:
    """Salva transcript em JSON em transcripts/. Retorna o caminho do arquivo."""
    transcript_dir = _transcripts_path()
    transcript_dir.mkdir(parents=True, exist_ok=True)
    guild_id = ticket.get("guild_id", "unknown")
    code = ticket.get("ticket_code", "unknown")
    filename = f"{guild_id}_{code}.json"
    filepath = transcript_dir / filename

    payload = {
        "ticket": ticket,
        "messages": messages,
    }
    _save_json(filepath, payload)
    return str(filepath)


def remove_transcript_file(filepath: str) -> bool:
    """Remove o arquivo de transcript do disco. Retorna True se removeu."""
    path = Path(filepath)
    if path.exists():
        try:
            path.unlink()
            return True
        except OSError:
            return False
    return False


def remove_closed_ticket_from_storage(channel_id: str) -> bool:
    """Remove o ticket (fechado) da lista em tickets.json para não acumular. Retorna True se removeu."""
    path = _data_path("tickets.json")
    data = _load_json(path, {})
    for guild_id, tickets in list(data.items()):
        new_list = [t for t in tickets if str(t.get("channel_id")) != str(channel_id)]
        if len(new_list) != len(tickets):
            data[guild_id] = new_list
            _save_json(path, data)
            return True
    return False
