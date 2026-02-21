"""Aviso de expiração das chaves de API (ex: Groq 90 dias)."""
import json
from datetime import datetime, timedelta
from pathlib import Path

DATA_DIR = Path(__file__).parent.parent / "data"
KEYS_FILE = DATA_DIR / "keys_expiry.json"

GROQ_DAYS_VALID = 90
WARN_DAYS_BEFORE = 14


def _load_keys_data() -> dict:
    if KEYS_FILE.exists():
        with open(KEYS_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    return {}


def _save_keys_data(data: dict) -> None:
    DATA_DIR.mkdir(exist_ok=True)
    with open(KEYS_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def register_key_first_seen(key_type: str) -> str | None:
    """
    Registra a data da primeira vez que a chave foi detectada.
    Retorna a data em ISO ou None.
    """
    data = _load_keys_data()
    field = f"{key_type}_first_seen"
    if field not in data:
        data[field] = datetime.utcnow().strftime("%Y-%m-%d")
        _save_keys_data(data)
    return data.get(field)


def get_days_remaining(key_type: str, days_valid: int = GROQ_DAYS_VALID) -> int | None:
    """
    Retorna dias restantes até a chave expirar.
    None se não houver data registrada.
    """
    data = _load_keys_data()
    field = f"{key_type}_first_seen"
    first_seen = data.get(field)
    if not first_seen:
        return None
    try:
        start = datetime.strptime(first_seen, "%Y-%m-%d")
        expiry = start + timedelta(days=days_valid)
        remaining = (expiry - datetime.utcnow()).days
        return max(0, remaining)
    except (ValueError, TypeError):
        return None


def should_warn_expiry(key_type: str, days_valid: int = GROQ_DAYS_VALID) -> tuple[bool, int | None]:
    """
    Retorna (deve_avisar, dias_restantes).
    Avisa quando <= WARN_DAYS_BEFORE dias.
    """
    days = get_days_remaining(key_type, days_valid)
    if days is None:
        return False, None
    return days <= WARN_DAYS_BEFORE, days


def get_expiry_warning_message(key_type: str, days_remaining: int) -> str:
    key_name = "Groq" if key_type == "groq" else key_type.upper()
    return (
        f"⚠️ **Aviso:** A chave da API **{key_name}** expira em **{days_remaining} dias**.\n"
        f"Renove em https://console.groq.com para evitar interrupção da IA do agente."
    )
