"""Tradução automática para tickets multilíngues.
Idioma do ticket é FIXO (escolhido na abertura) — sem detecção automática.
"""
from deep_translator import GoogleTranslator

try:
    from langdetect import detect, DetectorFactory
    DetectorFactory.seed = 0
    _HAS_LANGDETECT = True
except ImportError:
    _HAS_LANGDETECT = False

# Idioma do jogador (escolhido obrigatoriamente na abertura). value=único, (code_google, emoji, label)
# Google Translator: en, es, fr, de, it, pt, ru
TICKET_LANGUAGES = [
    ("en", "🇺🇸", "English (US)"),
    ("en-GB", "🇬🇧", "English (UK)"),
    ("es", "🇪🇸", "Spanish"),
    ("fr", "🇫🇷", "French"),
    ("de", "🇩🇪", "German"),
    ("it", "🇮🇹", "Italian"),
    ("pt-PT", "🇵🇹", "Portuguese (PT)"),
    ("pt", "🇧🇷", "Portuguese (BR)"),
    ("ru", "🇷🇺", "Russian"),
]


def lang_to_google_code(lang: str) -> str:
    """Mapeia author_lang para código do Google Translator."""
    m = {"en-GB": "en", "pt-PT": "pt"}
    return m.get(lang, lang)


def get_lang_options_for_select() -> list[tuple[str, str, str]]:
    """Retorna (value, emoji, label) para Select. value único para cada opção."""
    return list(TICKET_LANGUAGES)


def translate_text(text: str, target: str = "en", source: str = "auto") -> str:
    """
    Traduz texto para o idioma alvo.
    source='auto' detecta o idioma automaticamente.
    """
    if not text or not text.strip():
        return text
    try:
        translator = GoogleTranslator(source=source, target=target)
        return translator.translate(text)
    except Exception:
        return text


def translate_to_both(text: str) -> dict[str, str]:
    """
    Traduz texto para PT e EN (útil para salvar em transcript).
    Retorna {"original": "...", "pt": "...", "en": "..."}.
    """
    result = {"original": text, "pt": text, "en": text}
    if not text or not text.strip() or len(text) < 2:
        return result
    try:
        result["pt"] = GoogleTranslator(source="auto", target="pt").translate(text)
        result["en"] = GoogleTranslator(source="auto", target="en").translate(text)
    except Exception:
        pass
    return result


def detect_language(text: str) -> str:
    """
    Detecta o idioma do texto. Retorna código ISO (pt, en, es, etc).
    Retorna 'unknown' em caso de erro ou texto vazio.
    """
    if not text or len(text.strip()) < 3:
        return "unknown"
    if not _HAS_LANGDETECT:
        return "unknown"
    try:
        return detect(text)
    except Exception:
        return "unknown"


def translate_for_ticket(text: str, source: str, target: str) -> str | None:
    """
    Traduz texto entre idiomas para tickets.
    source/target: 'pt', 'en', 'es', etc.
    Retorna None em caso de erro.
    """
    if not text or not text.strip():
        return None
    if source == target:
        return text
    try:
        return GoogleTranslator(source=source, target=target).translate(text)
    except Exception:
        return None


def add_translation_to_message(content: str, target_lang: str = "en") -> str | None:
    """
    Se o conteúdo estiver em outro idioma, retorna a tradução.
    Útil para mostrar tradução em tempo real no ticket.
    Retorna None se não precisar traduzir (mesmo idioma) ou em caso de erro.
    """
    if not content or len(content) < 3:
        return None
    try:
        translated = translate_text(content, target=target_lang)
        if translated and translated != content:
            return translated
    except Exception:
        pass
    return None
