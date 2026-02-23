"""Gera transcript do ticket em HTML para download e arquivo (estilo bate-papo, idioma da categoria).
Também gera resumo em texto para envio no privado do jogador (identifica mensagens do bot/traduções)."""
import re
from datetime import datetime
from html import escape


# Nome exibido para mensagens do bot que não são atribuídas a staff (ex: claim, transfer, close)
_BOT_SYSTEM_LABEL = "Suporte Valley"

# Regex para extrair o texto real de mensagens "Suporte; Nome:** texto" (evita redundância no resumo)
_SUPPORT_SAY_PATTERN = re.compile(r"^\*\*(?:Suporte|Support)[^:]*:\*\*\s*", re.IGNORECASE)


# Textos da interface por idioma (categoria do ticket)
_LABELS = {
    "pt": {
        "title": "Transcript",
        "open": "Aberto",
        "closed": "Fechado",
        "server": "Servidor",
        "nick": "Nick",
        "steam_id": "Steam ID",
        "reason": "Motivo",
        "time": "Horário",
        "author": "Autor",
        "message": "Mensagem",
        "footer": "Suporte Valley • Transcript gerado em",
        "media": "mídia/arquivo",
    },
    "en": {
        "title": "Transcript",
        "open": "Opened",
        "closed": "Closed",
        "server": "Server",
        "nick": "Nick",
        "steam_id": "Steam ID",
        "reason": "Reason",
        "time": "Time",
        "author": "Author",
        "message": "Message",
        "footer": "Suporte Valley • Transcript generated on",
        "media": "media/file",
    },
}


def _format_content_for_html(text: str) -> str:
    """Escapa HTML e converte quebras de linha em <br>. Destaca @menções e @cargos com span."""
    if not text or not text.strip():
        return escape("")
    escaped = escape(text)
    # Quebras de linha
    escaped = escaped.replace("\n", "<br>\n")
    # Destacar @menções e @cargos (só quando @ inicia “token”, para não pegar e-mail)
    escaped = re.sub(
        r"(?<!\S)(@[\w\u00C0-\u024F\u1E00-\u1EFF\s\-\.]+)",
        r'<span class="mention">\1</span>',
        escaped,
        flags=re.UNICODE,
    )
    return escaped


def build_transcript_html(ticket: dict, messages: list[dict], guild_name: str = "Servidor") -> str:
    """
    Gera HTML do transcript em estilo bate-papo.
    - Menções e cargos já vêm como nomes (ex: @João, @Suporte), não como código.
    - Interface e texto principal no idioma da categoria do ticket (lang).
    """
    lang = ticket.get("lang", "pt")
    if lang not in _LABELS:
        lang = "pt"
    L = _LABELS[lang]

    code = escape(ticket.get("ticket_code", "N/A"))
    created = ticket.get("created_at", "")
    closed = ticket.get("closed_at", "")
    server_name = escape(ticket.get("server_name", "N/A"))
    reason = escape((ticket.get("reason") or "—")[:500])
    nick = escape(ticket.get("nick", "—"))
    steam_id = escape(ticket.get("steam_id", "—"))

    try:
        dt_created = datetime.fromisoformat(created.replace("Z", "+00:00")) if created else None
        dt_closed = datetime.fromisoformat(closed.replace("Z", "+00:00")) if closed else None
        created_str = dt_created.strftime("%d/%m/%Y %H:%M") if dt_created else created
        closed_str = dt_closed.strftime("%d/%m/%Y %H:%M") if dt_closed else closed
    except Exception:
        created_str = created
        closed_str = closed

    blocks = []
    for m in messages:
        author_name = escape(m.get("author_name", "?"))
        raw_content = (m.get("content") or "").strip() or L["media"]
        trans = m.get("translations") or {}
        pt = (trans.get("pt") or "").strip()
        en = (trans.get("en") or "").strip()

        # Texto principal no idioma da categoria
        if lang == "en" and en:
            main_text = en
        elif lang == "pt" and pt:
            main_text = pt
        else:
            main_text = raw_content

        ts = m.get("timestamp", "")
        try:
            dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
            ts_str = dt.strftime("%d/%m/%Y %H:%M")
        except Exception:
            ts_str = ts[:16] if ts else "—"

        main_html = _format_content_for_html(main_text)
        # Tradução alternativa (outro idioma) em linha menor, se existir e for diferente
        other_lang = "en" if lang == "pt" else "pt"
        other_text = (trans.get(other_lang) or "").strip()
        other_block = ""
        if other_text and other_text != main_text:
            other_html = _format_content_for_html(other_text)
            flag = "🇺🇸 EN" if other_lang == "en" else "🇧🇷 PT"
            other_block = f'<div class="translation">{flag}: {other_html}</div>'

        blocks.append(
            f"""
            <div class="msg-block">
                <div class="msg-meta">
                    <span class="msg-author">{author_name}</span>
                    <span class="msg-time">{ts_str}</span>
                </div>
                <div class="msg-body">
                    <div class="msg-content">{main_html}</div>
                    {other_block}
                </div>
            </div>"""
        )

    messages_html = "\n".join(blocks)

    html = f"""<!DOCTYPE html>
<html lang="{lang}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{L["title"]} {code} — {escape(guild_name)}</title>
    <style>
        * {{ box-sizing: border-box; }}
        body {{ font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; margin: 0; padding: 0; background: #313338; color: #dbdee1; line-height: 1.5; }}
        .container {{ max-width: 800px; margin: 0 auto; padding: 1rem; }}
        .header {{ background: #2b2d31; padding: 1.25rem; border-radius: 8px; margin-bottom: 1rem; border-left: 4px solid #5865F2; }}
        .header h1 {{ margin: 0 0 0.5rem 0; font-size: 1.35rem; font-weight: 600; }}
        .meta {{ font-size: 0.9rem; color: #b5bac1; }}
        .meta + .meta {{ margin-top: 0.25rem; }}
        .chat {{ background: #2b2d31; border-radius: 8px; padding: 0.75rem; }}
        .msg-block {{ margin-bottom: 1rem; padding: 0.5rem 0; border-bottom: 1px solid #383a40; }}
        .msg-block:last-child {{ border-bottom: none; margin-bottom: 0; }}
        .msg-meta {{ display: flex; align-items: baseline; gap: 0.5rem; margin-bottom: 0.35rem; flex-wrap: wrap; }}
        .msg-author {{ font-weight: 600; color: #f2f3f5; }}
        .msg-time {{ font-size: 0.8rem; color: #80848e; }}
        .msg-body {{ padding-left: 0; }}
        .msg-content {{ word-break: break-word; white-space: pre-wrap; }}
        .msg-content a {{ color: #00a8fc; text-decoration: none; }}
        .msg-content a:hover {{ text-decoration: underline; }}
        .mention {{ color: #00a8fc; font-weight: 500; }}
        .translation {{ font-size: 0.85rem; color: #949ba4; margin-top: 0.4rem; padding-left: 0.5rem; border-left: 2px solid #5865F2; }}
        .footer {{ margin-top: 1rem; font-size: 0.8rem; color: #80848e; text-align: center; }}
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>📑 {L["title"]} — {code}</h1>
            <div class="meta">{escape(guild_name)} • {L["open"]}: {created_str} • {L["closed"]}: {closed_str}</div>
            <div class="meta">{L["server"]}: {server_name} • {L["nick"]}: {nick} • {L["steam_id"]}: {steam_id}</div>
            <div class="meta">{L["reason"]}: {reason}</div>
        </div>
        <div class="chat">
            {messages_html}
        </div>
        <div class="footer">{L["footer"]} {datetime.utcnow().strftime("%d/%m/%Y %H:%M")} UTC</div>
    </div>
</body>
</html>"""
    return html


def build_transcript_summary(
    ticket: dict,
    messages: list[dict],
    guild_name: str = "Servidor",
    author_lang: str = "pt",
) -> str:
    """
    Gera resumo em texto de tudo que foi falado no ticket para envio no privado do jogador.
    Identifica mensagens enviadas pelo bot (traduções automáticas, sistema) e usa o idioma do autor.
    Formato: [HH:MM DD/MM/YYYY] @Nome: mensagem
    """
    code = ticket.get("ticket_code", "N/A")
    author_id = str(ticket.get("author_id", ""))
    author_name = "?"
    for m in messages:
        if str(m.get("author_id")) == author_id:
            author_name = m.get("author_name", "?")
            break

    lines = [f"RESUMO TICKET #{code} @{author_name}", ""]

    _lang_map = {"en-GB": "en", "pt-PT": "pt"}
    lang = _lang_map.get(author_lang, author_lang) if author_lang in ("pt", "en", "en-GB", "pt-PT") else "pt"
    other_lang = "en" if lang == "pt" else "pt"

    for m in messages:
        ts = m.get("timestamp", "")
        try:
            dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
            ts_str = dt.strftime("%H:%M %d/%m/%Y")
        except Exception:
            ts_str = ts[:16] if ts else "—"

        display_name = m.get("author_name", "?")
        if m.get("is_bot_message") and not m.get("is_staff_say"):
            display_name = _BOT_SYSTEM_LABEL

        raw_content = (m.get("content") or "").strip()
        trans = m.get("translations") or {}
        pt = (trans.get("pt") or "").strip()
        en = (trans.get("en") or "").strip()

        if lang == "en" and en:
            content = en
        elif lang == "pt" and pt:
            content = pt
        else:
            content = raw_content

        if m.get("is_staff_say") and content:
            content = _SUPPORT_SAY_PATTERN.sub("", content).strip()

        if not content:
            content = "—"

        lines.append(f"[{ts_str}] @{display_name}: {content}")

    return "\n".join(lines)
