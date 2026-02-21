"""Cache do último painel por canal (para apagar ao reabrir)."""
from collections import OrderedDict

from utils.limits import MAX_PANEL_CACHE

# OrderedDict para LRU: mais antigo é removido quando atingir limite
_panel_messages: OrderedDict[str, int] = OrderedDict()


def get_last_panel_message_id(channel_id: int | str) -> int | None:
    key = str(channel_id)
    if key not in _panel_messages:
        return None
    _panel_messages.move_to_end(key)
    return _panel_messages[key]


def set_last_panel_message_id(channel_id: int | str, message_id: int) -> None:
    key = str(channel_id)
    _panel_messages[key] = message_id
    _panel_messages.move_to_end(key)
    while len(_panel_messages) > MAX_PANEL_CACHE:
        _panel_messages.popitem(last=False)
