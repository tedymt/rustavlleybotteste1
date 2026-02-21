"""
Limites para evitar uso infinito de memória.
Todas as listas e caches do bot são limitados.
"""

# Agente
MAX_LEARNED_DATA = 500
MAX_TEACHINGS = 100
MAX_AGENT_CHANNELS = 50
MAX_AGENT_CATEGORIES = 20
MAX_INSTRUCTION_LENGTH = 4000
MAX_LEARNED_ITEM_CONTENT = 1000

# Tickets / Config
MAX_SERVERS = 30
MAX_ALLOWED_SUP_USERS = 50

# Cache em memória
MAX_PANEL_CACHE = 200

# Tickets por guilda (apenas abertos + últimos fechados)
MAX_TICKETS_PER_GUILD = 500
MAX_TRANSCRIPT_MESSAGES = 500


def trim_list(lst: list, max_size: int) -> list:
    """Retorna lista limitada aos últimos max_size itens."""
    if len(lst) <= max_size:
        return lst
    return lst[-max_size:]


def trim_config(config: dict) -> dict:
    """Aplica limites em listas da config da guilda."""
    for key, limit in [
        ("agent_learned_data", MAX_LEARNED_DATA),
        ("agent_teachings", MAX_TEACHINGS),
        ("agent_channels", MAX_AGENT_CHANNELS),
        ("agent_categories", MAX_AGENT_CATEGORIES),
        ("servers", MAX_SERVERS),
        ("allowed_sup_users", MAX_ALLOWED_SUP_USERS),
    ]:
        if key in config and isinstance(config[key], list):
            config[key] = trim_list(config[key], limit)
    if config.get("agent_instructions") and len(config["agent_instructions"]) > MAX_INSTRUCTION_LENGTH:
        config["agent_instructions"] = config["agent_instructions"][:MAX_INSTRUCTION_LENGTH]
    return config
