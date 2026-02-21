"""
Filtro anti-toxic: lista de palavras + IA para contexto.
A IA decide se a mensagem é ofensiva no contexto (ex: "que merda" = OK, "você é merda" = ofensa).
"""
import re

# Palavras que podem indicar toxicidade (pré-filtro para reduzir chamadas à API)
# A IA confirma se é ofensa no contexto
TOXIC_PT = frozenset({
    "caralho", "porra", "puta", "puto", "merda", "cacete", "foda", "fodase",
    "foda-se", "vai se foder", "vai tomar no cu", "vtnc", "vtmnc",
    "cu", "buceta", "bucetuda", "prega", "rapariga", "putinha", "putão",
    "vagabunda", "vagabundo", "filho da puta", "fdp", "arrombado", "viado",
    "viadão", "retardado", "retardada", "imbecil", "idiota",
    "escroto", "escrota", "nojento", "nojent", "babaca", "babaca", "otário",
    "otária", "mongol", "mongoloide", "deficiente", "bosta", "bostinha", "cuzão", "cusão",
})

TOXIC_EN = frozenset({
    "fuck", "fucking", "fucker", "fucked", "shit", "shitty", "ass", "asshole",
    "bitch", "bastard", "dick", "cock", "cunt", "pussy", "whore", "slut",
    "nigger", "nigga", "faggot", "fag", "retard", "retarded", "stupid",
    "idiot", "moron", "dumb", "sucker", "screw", "damn", "hell",
    "wtf", "stfu", "kys", "kill yourself", "die",
})

# Variações com leet/caracteres
_LEET_MAP = {"4": "a", "3": "e", "1": "i", "0": "o", "5": "s", "7": "t"}


def _normalize_word(word: str) -> str:
    """Remove caracteres especiais e normaliza leet."""
    w = re.sub(r"[^a-z0-9]", "", word.lower())
    for k, v in _LEET_MAP.items():
        w = w.replace(k, v)
    return w


def contains_toxic(text: str) -> bool:
    """Verifica se o texto contém palavrões (PT ou EN)."""
    if not text or len(text) < 2:
        return False
    lower = text.lower()
    words = re.findall(r"\b\w+\b", lower)
    for w in words:
        norm = _normalize_word(w)
        if norm in TOXIC_PT or norm in TOXIC_EN:
            return True
        for bad in TOXIC_PT | TOXIC_EN:
            if len(norm) >= 3 and (bad in norm or norm in bad):
                return True
    # Verifica substrings (ex: palavrão grudado)
    text_norm = _normalize_word(lower.replace(" ", ""))
    for bad in TOXIC_PT | TOXIC_EN:
        if len(bad) >= 4 and bad in text_norm:
            return True
    return False


def get_toxic_alert(lang: str) -> str:
    """Retorna mensagem de alerta no idioma do jogador."""
    alerts = {
        "pt": "Sua mensagem foi removida por conter linguagem ofensiva.",
        "en": "Your message was removed for containing offensive language.",
        "es": "Tu mensaje fue eliminado por contener lenguaje ofensivo.",
        "fr": "Votre message a été supprimé pour langage offensant.",
        "de": "Deine Nachricht wurde wegen beleidigender Sprache entfernt.",
        "it": "Il tuo messaggio è stato rimosso per linguaggio offensivo.",
        "ja": "攻撃的な言葉が含まれているため、メッセージは削除されました。",
    }
    return "⚠️ " + alerts.get(lang, alerts["en"])


def might_be_toxic(text: str) -> bool:
    """
    Pré-filtro rápido: verifica se o texto PODE ser tóxico.
    Se True, a IA deve analisar o contexto. Reduz chamadas desnecessárias.
    """
    return contains_toxic(text)
