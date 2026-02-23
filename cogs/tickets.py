"""
Sistema de Tickets do Suporte Valley.

- Armazenamento 100% em JSON (sem banco de dados)
- Tradução automática PT/EN nas mensagens e transcripts
- Config moderno com Select Menus (Categoria, Logs, Cargo)
- Segurança: só autor do ticket + cargo suporte podem responder
- Autor do ticket: ver, enviar mensagem, vídeo, link, histórico
"""
import asyncio
import io
import re
import random
import string
from datetime import datetime, timedelta

import discord
from discord import app_commands
from discord.ext import commands, tasks
from discord.enums import ChannelType
from discord.ui import ChannelSelect, RoleSelect, UserSelect, Modal, TextInput

from config import BOT_OWNER_ID
from utils.storage import (
    get_guild_config,
    save_guild_config,
    get_open_tickets,
    get_ticket_by_channel,
    add_ticket,
    update_ticket,
    append_ticket_translation,
    save_transcript,
    remove_transcript_file,
    remove_closed_ticket_from_storage,
)
from utils.transcript_html import build_transcript_html, build_transcript_summary
from utils.panel_cache import get_last_panel_message_id, set_last_panel_message_id
from utils.limits import MAX_ALLOWED_SUP_USERS, MAX_SERVERS, MAX_TRANSCRIPT_MESSAGES
from utils.translator import (
    translate_to_both,
    translate_text,
    translate_for_ticket,
    get_lang_options_for_select,
    lang_to_google_code,
    t,
)


# Steam ID 64: 17 dígitos, começa com 7656119 (padrão Steam)
_STEAM_ID64_PATTERN = re.compile(r"^7656119[0-9]{10}$")


def is_valid_steam_id64(value: str) -> tuple[bool, str]:
    """
    Valida Steam ID 64: 17 dígitos começando com 7656119. Ex: 76561198753318292
    Retorna (válido, valor_normalizado). Remove espaços antes de validar.
    """
    if not value or not value.strip():
        return False, ""
    cleaned = value.strip().replace(" ", "")
    if _STEAM_ID64_PATTERN.fullmatch(cleaned):
        return True, cleaned
    return False, cleaned


def generate_ticket_code() -> str:
    """Gera código único para o ticket (ex: SV-A3F9)."""
    return f"SV-{''.join(random.choices(string.ascii_uppercase + string.digits, k=4))}"


def format_duration(start: datetime, end: datetime) -> str:
    """Formata duração em horas e minutos."""
    delta = end - start
    hours, remainder = divmod(int(delta.total_seconds()), 3600)
    minutes = remainder // 60
    if hours > 0:
        return f"{hours}h {minutes}m"
    return f"{minutes}m"


def color_from_hex(hex_color: str) -> int:
    """Converte hex para int (cor Discord)."""
    hex_color = hex_color.lstrip("#")
    return int(hex_color, 16)


def _parse_iso(iso_str: str | None) -> datetime | None:
    """Retorna datetime a partir de string ISO ou None."""
    if not iso_str:
        return None
    try:
        return datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
    except (ValueError, TypeError):
        return None


async def _staff_view_interaction_check(interaction: discord.Interaction) -> bool:
    """Verifica se o usuário é staff. Retorna False se não for. Usado por views de ticket."""
    if not interaction.guild:
        return False
    member = interaction.user
    if not isinstance(member, discord.Member):
        try:
            member = await interaction.guild.fetch_member(interaction.user.id)
        except Exception:
            return False
    config = get_guild_config(str(interaction.guild.id))
    if not _is_staff(member, config):
        return False
    return True


class StaffTicketView(discord.ui.View):
    """View base com interaction_check — bloqueia não-staff em todos os botões."""

    def __init__(self, bot):
        super().__init__(timeout=None)
        self.bot = bot

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not await _staff_view_interaction_check(interaction):
            await interaction.response.send_message(
                "❌ Apenas a equipe de suporte pode usar estes botões.",
                ephemeral=True,
            )
            return False
        return True


class TicketView(StaffTicketView):
    """View com botões do ticket (Fechar, Assumir, etc.) — uso exclusivo do suporte."""

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        return await super().interaction_check(interaction)

    @discord.ui.button(
        label="Fechar / Close",
        style=discord.ButtonStyle.danger,
        emoji="🔒",
        custom_id="sv_close_ticket",
    )
    async def close_ticket(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("TicketCog")
        if cog:
            await cog._do_close_ticket(interaction)

    @discord.ui.button(
        label="Notificar no privado",
        style=discord.ButtonStyle.secondary,
        emoji="📩",
        custom_id="sv_notify_dm",
        row=1,
    )
    async def notify_dm(self, interaction: discord.Interaction, button: discord.ui.Button):
        await _notify_dm_button_callback(interaction)

    @discord.ui.button(
        label="Transferir",
        style=discord.ButtonStyle.secondary,
        emoji="↩️",
        custom_id="sv_transfer_ticket",
        row=1,
    )
    async def transfer_ticket(self, interaction: discord.Interaction, button: discord.ui.Button):
        await _transfer_ticket_button_callback(interaction)

    @discord.ui.button(
        label="Traduções",
        style=discord.ButtonStyle.secondary,
        emoji="🌐",
        custom_id="sv_translations",
        row=1,
    )
    async def translations_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await _translations_button_callback(interaction)

    @discord.ui.button(
        label="Alterar idioma",
        style=discord.ButtonStyle.secondary,
        emoji="🔤",
        custom_id="sv_change_lang",
        row=1,
    )
    async def change_lang_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await _change_lang_button_callback(interaction)

    @discord.ui.button(
        label="Auto-fechar (on/off)",
        style=discord.ButtonStyle.secondary,
        emoji="⏱️",
        custom_id="sv_toggle_autoclose",
        row=2,
    )
    async def toggle_autoclose_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await _toggle_autoclose_button_callback(interaction)

    @discord.ui.button(
        label="Assumir / Claim",
        style=discord.ButtonStyle.success,
        emoji="🙋",
        custom_id="sv_claim_ticket",
    )
    async def claim_ticket(self, interaction: discord.Interaction, button: discord.ui.Button):
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message(f"❌ {t('staff_only_claim', ticket.get('author_lang') or 'en')}", ephemeral=True)

        staff = interaction.user
        author_id = ticket.get("author_id")
        author_lang = ticket.get("author_lang") or "en"
        update_ticket(str(interaction.channel.id), {"staff_id": str(staff.id)})

        author_mention = f"<@{author_id}>"
        channel_msg = f"✅ {t('claim_channel', author_lang, mention=author_mention, staff=staff.display_name)}"
        await interaction.response.send_message(channel_msg, ephemeral=False)

        try:
            author = await self.bot.fetch_user(author_id)
            dm_title = f"✅ {t('claim_dm_title', author_lang)}"
            dm_desc = t("claim_dm_desc", author_lang, staff=staff.display_name)
            dm_embed = discord.Embed(title=dm_title, description=dm_desc, color=0x2ECC71)
            channel_link = f"https://discord.com/channels/{interaction.guild.id}/{interaction.channel.id}"
            dm_view = discord.ui.View()
            dm_btn_label = t("open_ticket", author_lang)
            dm_view.add_item(
                discord.ui.Button(
                    label=dm_btn_label,
                    style=discord.ButtonStyle.link,
                    url=channel_link,
                    emoji="🎫",
                )
            )
            await author.send(embed=dm_embed, view=dm_view)
        except discord.Forbidden:
            pass

        new_view = _build_claimed_view(self.bot, staff)
        await interaction.message.edit(view=new_view)


def _build_claimed_view(bot, staff_member: discord.Member) -> discord.ui.View:
    """View com botão Assumir desabilitado (Staff: nome) — usada após claim ou quando staff envia mensagem."""
    view = StaffTicketView(bot)
    close_btn = discord.ui.Button(label="Fechar / Close", style=discord.ButtonStyle.danger, emoji="🔒", custom_id="sv_close_ticket")
    close_btn.callback = _close_ticket_button_callback
    view.add_item(close_btn)
    notify_btn = discord.ui.Button(label="Notificar no privado", style=discord.ButtonStyle.secondary, emoji="📩", custom_id="sv_notify_dm", row=0)
    notify_btn.callback = _notify_dm_button_callback
    view.add_item(notify_btn)
    transfer_btn = discord.ui.Button(label="Transferir", style=discord.ButtonStyle.secondary, emoji="↩️", custom_id="sv_transfer_ticket", row=0)
    transfer_btn.callback = _transfer_ticket_button_callback
    view.add_item(transfer_btn)
    trans_btn = discord.ui.Button(label="Traduções", style=discord.ButtonStyle.secondary, emoji="🌐", custom_id="sv_translations", row=0)
    trans_btn.callback = _translations_button_callback
    view.add_item(trans_btn)
    chlang_btn = discord.ui.Button(label="Alterar idioma", style=discord.ButtonStyle.secondary, emoji="🔤", custom_id="sv_change_lang", row=1)
    chlang_btn.callback = _change_lang_button_callback
    view.add_item(chlang_btn)
    view.add_item(
        discord.ui.Button(label=f"Staff: {staff_member.name}", style=discord.ButtonStyle.success, emoji="✅", custom_id="sv_claimed", disabled=True)
    )
    return view


def _get_category_for_lang(config: dict, lang: str) -> str | None:
    """Retorna category_id para o idioma do painel (pt → BR, en → US). Fallback em category_id."""
    if lang == "en":
        return config.get("category_id_en") or config.get("category_id")
    return config.get("category_id_pt") or config.get("category_id")


def _detect_panel_lang_from_channel(channel: discord.abc.GuildChannel | None, config: dict) -> str:
    """Detecta o idioma do painel pela categoria do canal. Retorna 'en' ou 'pt'."""
    if not channel or not getattr(channel, "category_id", None):
        return "pt"
    cat_id = str(channel.category_id)
    cat_en = config.get("category_id_en")
    cat_pt = config.get("category_id_pt")
    if cat_en and cat_id == str(cat_en):
        return "en"
    if cat_pt and cat_id == str(cat_pt):
        return "pt"
    return "pt"


def can_use_sup(user_id: str, guild_id: str) -> bool:
    """Verifica se o usuário pode usar !sup (dono ou autorizado)."""
    if str(user_id) == BOT_OWNER_ID:
        return True
    config = get_guild_config(guild_id)
    return str(user_id) in config.get("allowed_sup_users", [])


def is_bot_owner(user_id: str) -> bool:
    """Verifica se é o dono do bot."""
    return str(user_id) == BOT_OWNER_ID


def _is_staff(member: discord.Member, config: dict) -> bool:
    """Verifica se o membro é staff (dono do bot, admin, cargo de suporte ou allowed_sup_users)."""
    if str(member.id) == BOT_OWNER_ID:
        return True
    if member.guild_permissions.administrator:
        return True
    if str(member.id) in config.get("allowed_sup_users", []):
        return True
    support_role_id = config.get("support_role_id")
    return bool(support_role_id and member.get_role(int(support_role_id)))


async def _notify_dm_button_callback(interaction: discord.Interaction):
    """Callback do botão Notificar no privado: delega para o Cog."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._do_notify_dm(interaction)


async def _transfer_ticket_button_callback(interaction: discord.Interaction):
    """Callback do botão Transferir: só staff; mostra select para escolher outro suporte."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._handle_transfer_click(interaction)


async def _close_ticket_button_callback(interaction: discord.Interaction):
    """Callback do botão Fechar quando a view é dinâmica (após Assumir/Transferir)."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._do_close_ticket(interaction)


async def _translations_button_callback(interaction: discord.Interaction):
    """Callback do botão Traduções: só staff; abre view com 3 opções (última, 5 últimas, completa)."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._do_translations_menu(interaction)


async def _change_lang_button_callback(interaction: discord.Interaction):
    """Callback do botão Alterar idioma: só staff; altera idioma do ticket manualmente (com log)."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._do_change_lang(interaction)


async def _toggle_autoclose_button_callback(interaction: discord.Interaction):
    """Callback do botão para ativar/desativar auto-fechamento por inatividade neste ticket (só staff)."""
    cog = interaction.client.get_cog("TicketCog")
    if cog:
        await cog._do_toggle_autoclose(interaction)


class TransferSelectView(discord.ui.View):
    """View com UserSelect para escolher um membro da equipe de suporte (só staff). Placeholder no idioma do ticket."""

    def __init__(self, bot, guild_id: str, channel_id: str, ticket_message_id: int, lang: str = "pt"):
        super().__init__(timeout=60)
        self.bot = bot
        self.guild_id = guild_id
        self.channel_id = channel_id
        self.ticket_message_id = ticket_message_id
        self._lang = lang
        placeholder = "Select a support team member..." if lang == "en" else "Selecione um membro da equipe de suporte..."
        select = UserSelect(
            placeholder=placeholder,
            custom_id="sv_transfer_select",
            max_values=1,
        )
        select.callback = self._on_select_callback
        self.add_item(select)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            msg = "❌ Only support staff can transfer this ticket." if self._lang == "en" else "❌ Apenas a equipe de suporte pode transferir o ticket."
            await interaction.response.send_message(msg, ephemeral=True)
            return False
        return True

    async def _on_select_callback(self, interaction: discord.Interaction):
        if not interaction.data or not interaction.data.get("values"):
            msg = "❌ No member selected." if self._lang == "en" else "❌ Nenhum membro selecionado."
            return await interaction.response.send_message(msg, ephemeral=True)
        user_id = int(interaction.data["values"][0])
        member = interaction.guild.get_member(user_id) or await interaction.guild.fetch_member(user_id)
        if not member:
            msg = "❌ Member not found." if self._lang == "en" else "❌ Membro não encontrado."
            return await interaction.response.send_message(msg, ephemeral=True)
        if member.bot:
            msg = "❌ Cannot transfer to a bot." if self._lang == "en" else "❌ Não é possível transferir para um bot."
            return await interaction.response.send_message(msg, ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(member, config):
            msg = "❌ You can only transfer to a support team member (support role or administrator)." if self._lang == "en" else "❌ Só é possível transferir para um membro da equipe de suporte (cargo de suporte ou administrador)."
            return await interaction.response.send_message(msg, ephemeral=True)
        cog = interaction.client.get_cog("TicketCog")
        if cog:
            await cog._do_transfer_ticket(interaction, member, self.channel_id, self.ticket_message_id)


class UnlockChatView(discord.ui.View):
    """Botão para desbloquear chat (só staff)."""

    def __init__(self, bot):
        super().__init__(timeout=None)
        self.bot = bot
        btn = discord.ui.Button(
            label="🔓 Desbloquear / Unlock",
            style=discord.ButtonStyle.success,
            custom_id="sv_unlock_chat",
        )
        btn.callback = self._unlock_callback
        self.add_item(btn)

    async def _unlock_callback(self, interaction: discord.Interaction):
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message("❌ Apenas a staff pode desbloquear.", ephemeral=True)
        channel = interaction.channel
        if not isinstance(channel, discord.TextChannel):
            return await interaction.response.send_message("❌ Canal inválido.", ephemeral=True)
        try:
            await channel.set_permissions(interaction.guild.default_role, overwrite=None, reason="!fechar (unlock)")
        except discord.Forbidden:
            return await interaction.response.send_message("❌ Sem permissão para desbloquear.", ephemeral=True)
        await interaction.response.send_message("✅ Chat desbloqueado.", ephemeral=True)
        try:
            await interaction.message.delete()
        except (discord.NotFound, discord.Forbidden, discord.HTTPException):
            pass
        notify = await channel.send(
            f"{t('chat_opened_lock', 'pt')}\n{t('chat_opened_lock', 'en')}"
        )
        asyncio.create_task(self._delete_notify_after(notify, 60))

    async def _delete_notify_after(self, msg: discord.Message, seconds: int):
        try:
            await asyncio.sleep(seconds)
            await msg.delete()
        except (discord.NotFound, discord.Forbidden, discord.HTTPException):
            pass


class TicketCog(commands.Cog):
    """Cog principal do sistema de tickets."""

    def __init__(self, bot):
        self.bot = bot

    async def cog_load(self):
        self.bot.add_view(TicketView(self.bot))
        self.bot.add_view(OpenTicketView(self.bot, {}))  # View persistente para botão abrir
        self.bot.add_view(UnlockChatView(self.bot))
        if not self._ticket_checks.is_running():
            self._ticket_checks.start()

    def cog_unload(self):
        self._ticket_checks.cancel()

    async def _do_close_ticket(self, interaction: discord.Interaction):
        """Anuncia tempo até encerrar e agenda o fechamento real (transcript, DM, logs)."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message(
                "❌ Ticket not found.", ephemeral=True
            )
        config = get_guild_config(str(interaction.guild.id))
        delay_sec = max(1, int(config.get("ticket_close_delay_seconds", 60)))
        author_lang = ticket.get("author_lang") or "en"
        close_confirm = f"🔒 **{t('close_confirm', author_lang, n=delay_sec)}**"
        await interaction.response.send_message(close_confirm, ephemeral=False)
        channel_id = str(interaction.channel.id)
        guild_id = str(interaction.guild.id)
        asyncio.create_task(self._close_ticket_after_delay(channel_id, guild_id, delay_sec, auto_close=False))

    async def _close_ticket_after_delay(self, channel_id: str, guild_id: str, delay_seconds: int, auto_close: bool = False, inactivity_reason: str = ""):
        """Aguarda o delay e executa o fechamento do ticket."""
        try:
            await asyncio.sleep(delay_seconds)
            # fetch_channel garante que o canal seja obtido mesmo fora do cache (get_channel pode retornar None após 60s)
            try:
                channel = await self.bot.fetch_channel(int(channel_id))
            except (discord.NotFound, discord.HTTPException):
                return
            if not isinstance(channel, discord.TextChannel):
                return
            ticket = get_ticket_by_channel(channel_id)
            if not ticket or ticket.get("status") not in ("OPEN", "CLOSING"):
                return
            await self._execute_close_ticket(channel, guild_id, ticket, auto_close=auto_close, inactivity_reason=inactivity_reason)
        except Exception as e:
            # Log para não perder o erro (a task não é awaited por ninguém)
            print(f"[Ticket] Erro ao fechar ticket após delay (channel={channel_id}): {e}")

    async def _execute_close_ticket(self, channel: discord.TextChannel, guild_id: str, ticket: dict, auto_close: bool = False, inactivity_reason: str = ""):
        """Executa transcript, DM, canal de transcripts, logs e deleta o canal."""
        # Coleta mensagens para transcript (menções e cargos como nomes, não código)
        messages_data = []
        say_msg_staff = ticket.get("say_msg_staff") or {}
        async for msg in channel.history(limit=500, oldest_first=True):
            content = msg.content or ""
            for u in (msg.mentions or []):
                content = content.replace(f"<@{u.id}>", f"@{u.display_name}").replace(f"<@!{u.id}>", f"@{u.display_name}")
            for r in getattr(msg, "role_mentions", []) or []:
                content = content.replace(f"<@&{r.id}>", f"@{r.name}")
            if msg.attachments:
                content += " " + " ".join(a.url for a in msg.attachments)
            raw_for_translate = (msg.content or "").strip()
            translations = await asyncio.to_thread(translate_to_both, raw_for_translate) if raw_for_translate else {"pt": "", "en": ""}

            author_id = str(msg.author.id)
            author_name = msg.author.display_name or str(msg.author)
            is_bot_message = msg.author.bot
            staff_id = say_msg_staff.get(str(msg.id))
            if staff_id:
                try:
                    staff_user = await self.bot.fetch_user(int(staff_id))
                    author_id = str(staff_user.id)
                    author_name = getattr(staff_user, "global_name", None) or staff_user.name or str(staff_user)
                except (discord.NotFound, ValueError, TypeError):
                    pass

            messages_data.append({
                "author_id": author_id,
                "author_name": author_name,
                "content": content,
                "translations": translations if raw_for_translate else {},
                "timestamp": msg.created_at.isoformat(),
                "is_bot_message": is_bot_message,
                "is_staff_say": bool(staff_id),
            })

        closed_at = datetime.utcnow()
        duration = format_duration(datetime.fromisoformat(ticket["created_at"]), closed_at)
        ticket_updated = {**ticket, "status": "CLOSED", "closed_at": closed_at.isoformat()}
        messages_for_transcript = messages_data[-MAX_TRANSCRIPT_MESSAGES:]
        transcript_path = save_transcript(ticket_updated, messages_for_transcript)
        update_ticket(str(channel.id), {"status": "CLOSED", "closed_at": closed_at.isoformat()})

        config = get_guild_config(guild_id)
        guild = channel.guild
        guild_name = guild.name
        code = ticket.get("ticket_code", "N/A")
        author = None
        try:
            author = await self.bot.fetch_user(ticket["author_id"])
        except (discord.NotFound, ValueError):
            pass
        author_lang = ticket.get("author_lang") or "en"

        html_content = build_transcript_html(ticket_updated, messages_for_transcript, guild_name)
        html_filename = f"transcript_{code}.html"
        summary_content = build_transcript_summary(
            ticket_updated, messages_for_transcript, guild_name,
            author_lang=author_lang,
        )
        if author:
            try:
                if auto_close and inactivity_reason:
                    dm_text = inactivity_reason
                else:
                    dm_text = f"✅ **{t('closed_dm', author_lang, guild=guild_name, code=code, duration=duration)}**"
                embeds_dm = []
                _desc_limit = 4000
                lines = summary_content.split("\n")
                parts = []
                current = []
                size = 0
                for line in lines:
                    line_len = len(line) + 1
                    if size + line_len > _desc_limit and current:
                        parts.append("\n".join(current))
                        current = []
                        size = 0
                    current.append(line)
                    size += line_len
                if current:
                    parts.append("\n".join(current))
                summary_title = t("summary_title", author_lang, code=code)
                summary_cont = t("summary_cont", author_lang, code=code)
                for i, part in enumerate(parts):
                    title = f"📋 {summary_title}" if i == 0 else f"📋 {summary_cont}"
                    embeds_dm.append(
                        discord.Embed(
                            title=title,
                            description=part,
                            color=color_from_hex(config.get("color", "#5865F2")),
                            timestamp=datetime.utcnow(),
                        )
                    )
                await author.send(dm_text, embeds=embeds_dm[:10])
            except discord.Forbidden:
                pass

        transcript_channel_id = config.get("transcript_channel_id")
        if transcript_channel_id:
            ch = guild.get_channel(int(transcript_channel_id))
            if ch and isinstance(ch, discord.TextChannel):
                try:
                    file_canal = discord.File(fp=io.BytesIO(html_content.encode("utf-8")), filename=html_filename)
                    desc_canal = t("transcript_desc", author_lang, user=f"<@{ticket['author_id']}>", duration=duration)
                    footer_canal = t("transcript_footer", author_lang)
                    embed_canal = discord.Embed(
                        title=f"📑 Transcript — {code}",
                        description=desc_canal,
                        color=color_from_hex(config.get("color", "#5865F2")),
                        timestamp=datetime.utcnow(),
                    )
                    embed_canal.set_footer(text=footer_canal)
                    await ch.send(embed=embed_canal, file=file_canal)
                except discord.Forbidden:
                    pass

        remove_transcript_file(transcript_path)
        remove_closed_ticket_from_storage(str(channel.id))

        if config.get("logs_channel_id"):
            log_channel = guild.get_channel(int(config["logs_channel_id"]))
            if log_channel:
                staff = await self.bot.fetch_user(ticket["staff_id"]) if ticket.get("staff_id") else None
                log_title = f"📑 {t('log_title_auto' if auto_close else 'log_title', author_lang)}"
                log_footer = t("log_footer", author_lang)
                embed = discord.Embed(
                    title=log_title,
                    color=color_from_hex(config.get("color", "#5865F2")),
                    timestamp=datetime.utcnow(),
                )
                embed.add_field(name=t("protocol", author_lang), value=f"`{code}`", inline=True)
                embed.add_field(name=t("user", author_lang), value=str(author or ticket["author_id"]), inline=True)
                embed.add_field(name="Staff", value=str(staff or "N/A"), inline=True)
                embed.add_field(name=t("duration", author_lang), value=duration, inline=True)
                embed.set_footer(text=log_footer)
                await log_channel.send(embed=embed)

        await channel.delete(reason="Ticket closed (auto)" if auto_close else "Ticket closed")

    async def _enter_maintenance_mode(self, guild_id: str) -> int:
        """Ativa modo manutenção, fecha todos os tickets abertos e retorna a quantidade fechada."""
        config = get_guild_config(guild_id)
        config["maintenance_mode"] = True
        save_guild_config(guild_id, config)

        open_tickets = get_open_tickets(guild_id)
        for ticket in open_tickets:
            channel_id = str(ticket.get("channel_id"))
            code = ticket.get("ticket_code", "N/A")
            author_lang = ticket.get("author_lang") or "en"
            reason = t("maintenance_close_dm", author_lang, code=code)
            update_ticket(channel_id, {"status": "CLOSING"})
            asyncio.create_task(
                self._close_ticket_after_delay(channel_id, guild_id, 0, auto_close=True, inactivity_reason=reason)
            )
        return len(open_tickets)

    async def _exit_maintenance_mode(self, guild_id: str) -> None:
        """Desativa modo manutenção."""
        config = get_guild_config(guild_id)
        config["maintenance_mode"] = False
        save_guild_config(guild_id, config)

    @commands.command(name="tickettest")
    async def ticket_test_command(self, ctx: commands.Context, mode: str = "unanswered"):
        """Força os checks de tempo para o ticket atual (teste de notificações). Uso: !tickettest [unanswered|reminder|autoclose|all]."""
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Sem permissão para usar este comando.", delete_after=5)

        ticket = get_ticket_by_channel(str(ctx.channel.id))
        if not ticket or ticket.get("status") != "OPEN":
            return await ctx.send("❌ Este comando só pode ser usado dentro de um canal de ticket **aberto**.", delete_after=8)

        mode = (mode or "unanswered").lower()
        valid = {"unanswered", "reminder", "autoclose", "all"}
        if mode not in valid:
            return await ctx.send("❌ Modo inválido. Use: `unanswered`, `reminder`, `autoclose` ou `all`.", delete_after=10)

        # Se o auto-fechamento estiver desativado para este ticket, o modo autoclose/all não terá efeito
        if mode in ("autoclose", "all") and ticket.get("auto_close_disabled", False):
            return await ctx.send(
                "⚠️ Auto-fechamento por inatividade está **desativado** para este ticket.\n"
                "Ative o botão `Auto-fechar (on/off)` no ticket para testar o autoclose.",
                delete_after=12,
            )

        config = get_guild_config(str(ctx.guild.id))
        channel_id = str(ctx.channel.id)
        now = datetime.utcnow()

        unanswered_min = max(1, int(config.get("ticket_unanswered_alert_minutes", 240)))
        staff_reminder_min = max(1, int(config.get("ticket_staff_reminder_minutes", 60)))
        author_inactivity_min = max(1, int(config.get("ticket_author_inactivity_close_minutes", 480)))

        updates: dict[str, str] = {}

        # 1) Simular ticket antigo sem resposta do staff
        if mode in ("unanswered", "all"):
            created_past = now - timedelta(minutes=unanswered_min + 2)
            updates["created_at"] = created_past.isoformat()
            # Garante que seja interpretado como "sem resposta do staff"
            updates["first_staff_message_at"] = ""
            updates["unanswered_alert_sent_at"] = ""

        # 2) Simular lembrete para staff (última mensagem do jogador)
        if mode in ("reminder", "all"):
            staff_time = now - timedelta(minutes=staff_reminder_min + 2)
            author_time = now
            staff_id = ticket.get("staff_id") or str(ctx.author.id)
            updates["staff_id"] = str(staff_id)
            updates.setdefault("first_staff_message_at", staff_time.isoformat())
            updates["last_staff_message_at"] = staff_time.isoformat()
            updates["last_author_message_at"] = author_time.isoformat()
            updates["last_staff_reminder_at"] = ""

        # 3) Simular auto-fechamento por inatividade do jogador
        if mode in ("autoclose", "all"):
            staff_time = now - timedelta(minutes=author_inactivity_min + 2)
            author_time = staff_time - timedelta(minutes=1)
            updates.setdefault("first_staff_message_at", staff_time.isoformat())
            updates["last_staff_message_at"] = staff_time.isoformat()
            updates["last_author_message_at"] = author_time.isoformat()

        if updates:
            update_ticket(channel_id, updates)

        await ctx.send(f"🔧 Dados de tempo ajustados para teste (`mode={mode}`). Rodando verificações agora...", delete_after=10)
        await self._run_ticket_checks_once()

    @tasks.loop(minutes=1)
    async def _ticket_checks(self):
        """Loop periódico: chama a verificação de tickets a cada minuto."""
        await self.bot.wait_until_ready()
        await self._run_ticket_checks_once()

    async def _is_ticket_channel_still_open(self, guild: discord.Guild, channel_id: str, config: dict) -> bool:
        """Verifica se o canal do ticket ainda existe e está na categoria de tickets (evita notificar tickets fechados/deletados)."""
        try:
            channel = await guild.fetch_channel(int(channel_id))
        except (discord.NotFound, discord.HTTPException):
            return False
        if not isinstance(channel, discord.TextChannel):
            return False
        cat_ids = [
            str(c) for c in [
                config.get("category_id"),
                config.get("category_id_pt"),
                config.get("category_id_en"),
            ]
            if c
        ]
        if not cat_ids:
            return True
        return channel.category_id is not None and str(channel.category_id) in cat_ids

    async def _run_ticket_checks_once(self):
        """Verifica tickets abertos: alerta suporte sem resposta, lembrete para staff, auto-fechamento por inatividade."""
        for guild in self.bot.guilds:
            config = get_guild_config(str(guild.id))
            open_tickets = get_open_tickets(str(guild.id))
            support_role_id = config.get("support_role_id")
            unanswered_min = max(1, int(config.get("ticket_unanswered_alert_minutes", 240)))
            staff_reminder_min = max(1, int(config.get("ticket_staff_reminder_minutes", 60)))
            author_inactivity_min = max(1, int(config.get("ticket_author_inactivity_close_minutes", 480)))
            now = datetime.utcnow()

            for ticket in open_tickets:
                channel_id = str(ticket.get("channel_id"))
                author_id = ticket.get("author_id")
                lang = ticket.get("lang", "pt")
                created_at = _parse_iso(ticket.get("created_at"))
                last_staff_at = _parse_iso(ticket.get("last_staff_message_at"))
                last_author_at = _parse_iso(ticket.get("last_author_message_at"))
                first_staff_at = _parse_iso(ticket.get("first_staff_message_at"))

                # 1) Ticket sem nenhuma resposta do staff há mais de N minutos → notifica suporte no privado (sempre em PT-BR, com botão para o ticket)
                if not first_staff_at and created_at:
                    mins_open = (now - created_at).total_seconds() / 60
                    if mins_open >= unanswered_min:
                        alerted_at = _parse_iso(ticket.get("unanswered_alert_sent_at"))
                        if not alerted_at or (now - alerted_at).total_seconds() / 60 >= 60:
                            if await self._is_ticket_channel_still_open(guild, channel_id, config) and support_role_id:
                                role = guild.get_role(int(support_role_id))
                                if role:
                                    channel_link = f"https://discord.com/channels/{guild.id}/{channel_id}"
                                    view = discord.ui.View()
                                    view.add_item(
                                        discord.ui.Button(
                                            label="Abrir ticket",
                                            style=discord.ButtonStyle.link,
                                            url=channel_link,
                                            emoji="🎫",
                                        )
                                    )
                                    proto = ticket.get("ticket_code", "N/A")
                                    for m in role.members:
                                        if m.bot:
                                            continue
                                        try:
                                            # Sempre em PT-BR para staff/admin
                                            await m.send(
                                                f"⚠️ **Ticket sem resposta** há mais de {unanswered_min} min.\n"
                                                f"Protocolo: `{proto}`",
                                                view=view,
                                            )
                                        except (discord.Forbidden, discord.HTTPException):
                                            pass
                                    update_ticket(channel_id, {"unanswered_alert_sent_at": now.isoformat()})

                # 2) Staff assumiu; última mensagem foi do jogador → lembrete a cada N min no privado do staff (sempre em PT-BR, com botão)
                staff_id = ticket.get("staff_id")
                if staff_id and last_author_at and last_staff_at and last_author_at >= last_staff_at:
                    mins_since_staff = (now - last_staff_at).total_seconds() / 60
                    if mins_since_staff >= staff_reminder_min:
                        last_reminder = _parse_iso(ticket.get("last_staff_reminder_at"))
                        if not last_reminder or (now - last_reminder).total_seconds() / 60 >= staff_reminder_min:
                            if not await self._is_ticket_channel_still_open(guild, channel_id, config):
                                pass  # Ticket fechado/deletado, não envia lembrete
                            else:
                                try:
                                    staff_member = await guild.fetch_member(int(staff_id))
                                    if staff_member and not staff_member.bot:
                                        channel_link = f"https://discord.com/channels/{guild.id}/{channel_id}"
                                        view = discord.ui.View()
                                        view.add_item(
                                            discord.ui.Button(
                                                label="Abrir ticket",
                                                style=discord.ButtonStyle.link,
                                                url=channel_link,
                                                emoji="🎫",
                                            )
                                        )
                                        # Sempre em PT-BR para staff/admin
                                        await staff_member.send(
                                            "⏰ **Lembrete:** Você tem um ticket aberto aguardando sua resposta.\n"
                                            "A última mensagem foi do jogador.",
                                            view=view,
                                        )
                                    update_ticket(channel_id, {"last_staff_reminder_at": now.isoformat()})
                                except (discord.NotFound, discord.Forbidden, discord.HTTPException):
                                    pass

                # 3) Staff respondeu; jogador não respondeu há mais de N min → auto-fecha e notifica autor no privado
                if not ticket.get("auto_close_disabled", False):
                    staff_replied_last = first_staff_at and last_staff_at and (last_author_at is None or last_staff_at >= last_author_at)
                    if staff_replied_last:
                        mins_since_author = (now - last_staff_at).total_seconds() / 60
                        if mins_since_author >= author_inactivity_min:
                            ticket_fresh = get_ticket_by_channel(channel_id)
                            if ticket_fresh and ticket_fresh.get("status") == "OPEN":
                                update_ticket(channel_id, {"status": "CLOSING"})
                                if lang == "en":
                                    inactivity_reason = (
                                        f"⏱️ **This ticket was closed automatically** on **{guild.name}** due to no response from you for over {author_inactivity_min} minutes after the support team's last message.\n\n"
                                        f"**Protocol:** `{ticket_fresh.get('ticket_code', 'N/A')}`\n\n"
                                        "📄 **Ticket summary below.**"
                                    )
                                else:
                                    inactivity_reason = (
                                        f"⏱️ **Este ticket foi fechado automaticamente** em **{guild.name}** por não haver resposta sua por mais de {author_inactivity_min} minutos após a última mensagem da equipe.\n\n"
                                        f"**Protocolo:** `{ticket_fresh.get('ticket_code', 'N/A')}`\n\n"
                                        "📄 **Resumo do ticket abaixo.**"
                                    )
                                # Usa o mesmo fluxo de fechamento (com delay 0) para aproveitar fetch_channel e logs
                                asyncio.create_task(self._close_ticket_after_delay(
                                    channel_id, str(guild.id), 0,
                                    auto_close=True, inactivity_reason=inactivity_reason
                                ))

    @commands.command(name="sup")
    async def sup_command(self, ctx: commands.Context):
        """Comando principal: !sup — abre menu de categorias (Ticket | Config Bot)."""
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Você não tem permissão para usar este comando.", delete_after=5)

        try:
            await ctx.message.delete()
        except discord.Forbidden:
            pass

        last_id = get_last_panel_message_id(ctx.channel.id)
        if last_id:
            try:
                old_msg = await ctx.channel.fetch_message(last_id)
                await old_msg.delete()
            except (discord.NotFound, discord.Forbidden):
                pass

        view = SupMainView(self.bot, str(ctx.guild.id))
        embed = _main_embed()
        msg = await ctx.send(embed=embed, view=view)
        set_last_panel_message_id(ctx.channel.id, msg.id)

    @commands.command(name="clean", aliases=["clena", "limpar"])
    async def clean_command(self, ctx: commands.Context, quantidade: int = 10):
        """Apaga mensagens no canal. Uso: !clean [número] (padrão: 10, máx: 50)."""
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Sem permissão.", delete_after=5)
        quantidade = min(max(1, quantidade), 50)
        try:
            await ctx.message.delete()
        except discord.Forbidden:
            pass
        import asyncio
        deleted = 0
        async for msg in ctx.channel.history(limit=quantidade + 1):
            try:
                await msg.delete()
                deleted += 1
            except (discord.Forbidden, discord.HTTPException):
                break
            await asyncio.sleep(0.55)
        confirm = await ctx.send(f"🧹 **{deleted}** mensagem(ns) removida(s).", delete_after=5)
        try:
            await confirm.delete(delay=5)
        except discord.Forbidden:
            pass

    def _parse_lock_duration(self, arg: str) -> int | None:
        """Converte argumento em segundos. Ex: '20' → 20min, '1d' → 24h, '2h' → 2h. Retorna None se inválido."""
        if not arg or not arg.strip():
            return None
        s = arg.strip().lower()
        try:
            if s.endswith("d"):
                return int(s[:-1]) * 86400
            if s.endswith("h"):
                return int(s[:-1]) * 3600
            if s.endswith("m"):
                return int(s[:-1]) * 60
            return int(s) * 60
        except ValueError:
            return None

    @commands.command(name="fechar", aliases=["lock", "trancar"])
    async def fechar_command(self, ctx: commands.Context, duracao: str = "30"):
        """Fecha o chat por um tempo. Uso: !fechar (30 min padrão) | !fechar 20 | !fechar 1d."""
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Sem permissão.", delete_after=5)
        if not isinstance(ctx.channel, discord.TextChannel):
            return await ctx.send("❌ Use em um canal de texto.", delete_after=5)
        seconds = self._parse_lock_duration(duracao)
        if seconds is None or seconds < 60:
            return await ctx.send("❌ Duração inválida. Ex: `!fechar` (30 min) ou `!fechar 1d` (24h)", delete_after=8)
        if seconds > 7 * 86400:
            return await ctx.send("❌ Máximo 7 dias (7d).", delete_after=5)
        try:
            await ctx.message.delete()
        except discord.Forbidden:
            pass
        channel = ctx.channel
        guild = ctx.guild

        cutoff = datetime.utcnow() - timedelta(minutes=30)
        async for msg in channel.history(limit=200, oldest_first=False):
            if msg.created_at.replace(tzinfo=None) <= cutoff:
                break
            try:
                await msg.delete()
                await asyncio.sleep(0.55)
            except (discord.Forbidden, discord.HTTPException):
                break

        try:
            await channel.set_permissions(guild.default_role, send_messages=False, reason="!fechar")
        except discord.Forbidden:
            return await ctx.send("❌ Sem permissão para alterar o canal.", delete_after=5)
        msg_pt = t("chat_closed_lock", "pt")
        msg_en = t("chat_closed_lock", "en")
        view = UnlockChatView(self.bot)
        lock_msg = await channel.send(f"{msg_pt}\n{msg_en}", view=view)
        asyncio.create_task(self._unlock_channel_after(channel.id, guild.id, seconds, lock_msg.id))

    async def _unlock_channel_after(self, channel_id: int, guild_id: int, seconds: int, lock_msg_id: int | None = None):
        """Desbloqueia o canal após o delay, remove a mensagem do bot e envia aviso temporário."""
        try:
            await asyncio.sleep(seconds)
            guild = self.bot.get_guild(guild_id)
            if not guild:
                return
            channel = guild.get_channel(channel_id)
            if not channel or not isinstance(channel, discord.TextChannel):
                return
            await channel.set_permissions(guild.default_role, overwrite=None, reason="!fechar (auto-unlock)")
            if lock_msg_id:
                try:
                    msg = await channel.fetch_message(lock_msg_id)
                    await msg.delete()
                except (discord.NotFound, discord.Forbidden, discord.HTTPException):
                    pass
            notify = await channel.send(
                f"{t('chat_opened_lock', 'pt')}\n{t('chat_opened_lock', 'en')}"
            )
            await asyncio.sleep(60)
            try:
                await notify.delete()
            except (discord.NotFound, discord.Forbidden, discord.HTTPException):
                pass
        except Exception as e:
            print(f"[Ticket] Erro ao desbloquear canal {channel_id}: {e}")

    @commands.Cog.listener()
    async def on_message(self, message: discord.Message):
        """Segurança: só autor do ticket ou cargo suporte podem enviar. Tradução invisível em background."""
        if message.author.bot:
            return

        ticket = get_ticket_by_channel(str(message.channel.id))
        if not ticket or ticket.get("status") != "OPEN":
            return

        config = get_guild_config(str(message.guild.id))
        support_role_id = config.get("support_role_id")
        author_id = str(ticket.get("author_id"))
        member = message.author
        content = (message.content or "").strip()
        author_lang = ticket.get("author_lang") or "en"

        # Autor do ticket (jogador)
        if str(member.id) == author_id:
            now_iso = datetime.utcnow().isoformat()
            updates = {"last_author_message_at": now_iso}
            if (ticket.get("reason") or "").strip() == "" and content:
                updates["reason"] = content[:500].strip()
            update_ticket(str(message.channel.id), updates)
            # Traduz em background, salva, atualiza painel staff. NÃO envia nada no chat.
            if content and len(content) >= 2:
                asyncio.create_task(self._translate_player_message_background(
                    message, content, author_id, author_lang,
                ))
            return

        # Staff (mesmo critério do botão Assumir)
        is_staff = _is_staff(member, config)
        if is_staff:
            now_iso = datetime.utcnow().isoformat()
            updates = {"last_staff_message_at": now_iso, "staff_id": str(member.id)}
            if not ticket.get("first_staff_message_at"):
                updates["first_staff_message_at"] = now_iso
            update_ticket(str(message.channel.id), updates)
            if content:
                try:
                    await message.delete()
                    target_code = lang_to_google_code(author_lang)
                    if target_code == "pt":
                        say_text = content
                    else:
                        say_text = await asyncio.to_thread(translate_for_ticket, content, "pt", target_code) or content
                    sent = await message.channel.send(f"**Suporte; {member.display_name}:** {say_text[:1900]}")
                    append_ticket_translation(
                        str(message.channel.id), sent.id, content, say_text, str(member.id), is_player=False
                    )
                    say_map = dict(ticket.get("say_msg_staff") or {})
                    say_map[str(sent.id)] = str(member.id)
                    if len(say_map) > 200:
                        say_map = dict(list(say_map.items())[-200:])
                    update_ticket(str(message.channel.id), {"say_msg_staff": say_map})
                except Exception:
                    pass
            ticket_msg_id = ticket.get("ticket_message_id")
            if ticket_msg_id:
                try:
                    ticket_msg = await message.channel.fetch_message(int(ticket_msg_id))
                    new_view = _build_claimed_view(self.bot, member)
                    await ticket_msg.edit(view=new_view)
                except (discord.NotFound, discord.Forbidden, discord.HTTPException):
                    pass
            return

        # Sem permissão
        try:
            await message.delete()
            warn_text = f"⚠️ {member.mention} — {t('only_author_support', ticket.get('author_lang') or 'en')}"
            warn = await message.channel.send(warn_text, delete_after=5)
            await warn.delete(delay=5)
        except discord.Forbidden:
            pass

    async def _translate_player_message_background(self, message: discord.Message, content: str, author_id: str, author_lang: str):
        """Traduz mensagem do jogador para PT (staff lê), salva e atualiza painel. Nunca exibe no chat."""
        try:
            translated = content
            if author_lang != "pt":
                translated = await asyncio.to_thread(translate_text, content, "pt", "auto") or content
            append_ticket_translation(str(message.channel.id), message.id, content, translated, author_id, is_player=True)
            await self._update_staff_panel(message.channel, translated, message.author.display_name or str(message.author))
        except Exception:
            pass

    async def _update_staff_panel(self, channel: discord.TextChannel, last_translated: str, author_name: str):
        """Atualiza embed do painel staff com a última mensagem do jogador traduzida."""
        ticket = get_ticket_by_channel(str(channel.id))
        if not ticket:
            return
        panel_id = ticket.get("staff_panel_message_id")
        if not panel_id:
            return
        try:
            panel_msg = await channel.fetch_message(panel_id)
            embed = discord.Embed(
                title="📋 Painel Staff — Última mensagem do jogador (traduzida)",
                description=last_translated[:4000] if last_translated else "*Nenhuma mensagem ainda*",
                color=0x3498DB,
            )
            embed.add_field(name="Jogador", value=author_name, inline=True)
            embed.add_field(name="Idioma do ticket", value=(ticket.get("author_lang") or "en").upper(), inline=True)
            embed.set_footer(text="Atualização automática")
            await panel_msg.edit(embed=embed)
        except (discord.NotFound, discord.Forbidden, discord.HTTPException):
            pass

    _preticket_cache = {}  # (user_id, guild_id) -> server_id (seleção pré-setada)
    _preticket_lang_cache = {}  # (user_id, guild_id) -> lang

    async def _handle_open_ticket_start(self, interaction: discord.Interaction, custom_id: str, lang: str | None = None):
        """Inicia fluxo: se houver servidores, mostra Select para escolher; depois modal com nick e Steam ID. Motivo é perguntado pela IA no canal após abrir."""
        guild = interaction.guild
        user = interaction.user
        config = get_guild_config(str(guild.id))
        if config.get("category_id_en") or config.get("category_id_pt"):
            lang = _detect_panel_lang_from_channel(interaction.channel, config)
        lang = lang or "pt"

        if config.get("maintenance_mode"):
            msg = t("maintenance_block", lang)
            return await interaction.response.send_message(msg, ephemeral=True)

        open_tickets = get_open_tickets(str(guild.id))
        if any(str(t.get("author_id")) == str(user.id) for t in open_tickets):
            msg = "❌ You already have an open ticket." if lang == "en" else "❌ Você já possui um ticket aberto."
            return await interaction.response.send_message(msg, ephemeral=True)

        servers = config.get("servers", [])
        if servers:
            key = (str(user.id), str(guild.id))
            TicketCog._preticket_lang_cache[key] = lang
            view = PreTicketServerView(self.bot, str(guild.id), config, lang)
            intro = "Select the server below, then choose your language." if lang == "en" else "Selecione o servidor abaixo e escolha o idioma."
            await interaction.response.send_message(intro, view=view, ephemeral=True)
        else:
            category_id = _get_category_for_lang(config, lang)
            view = PreTicketLanguageSelectView(
                self.bot, str(guild.id), config, lang,
                server_id="", server_name="N/A", category_id=category_id,
            )
            intro = "Select your language for this ticket:" if lang == "en" else "Selecione o idioma do atendimento:"
            await interaction.response.send_message(intro, view=view, ephemeral=True)

    async def _create_ticket(self, interaction: discord.Interaction, user: discord.Member,
                             server_id: str, server_name: str, category_id: str | None, nick: str, steam_id: str, reason: str = "", lang: str = "pt", author_lang: str = "en"):
        """Cria o canal de ticket com os dados do modal. lang = idioma do painel (pt/en). Motivo é perguntado pela IA no canal após abertura."""
        guild = interaction.guild
        config = get_guild_config(str(guild.id))

        ticket_code = generate_ticket_code()
        clean_server = (server_name or "suporte")[:10].lower().replace(" ", "-")
        channel_name = f"ticket-{clean_server}-{user.name}".lower()[:32]

        author_perms = discord.PermissionOverwrite(
            view_channel=True, send_messages=True, attach_files=True,
            embed_links=True, read_message_history=True,
        )
        support_perms = discord.PermissionOverwrite(
            view_channel=True, send_messages=True, attach_files=True,
            embed_links=True, read_message_history=True, manage_messages=True,
        )
        overwrites = {
            guild.default_role: discord.PermissionOverwrite(view_channel=False),
            user: author_perms,
        }
        support_role_id = config.get("support_role_id")
        if support_role_id:
            role = guild.get_role(int(support_role_id))
            if role:
                overwrites[role] = support_perms

        category = None
        if category_id:
            try:
                cat = guild.get_channel(int(category_id))
                if cat and isinstance(cat, discord.CategoryChannel):
                    category = cat
            except (ValueError, TypeError):
                pass

        try:
            ticket_channel = await guild.create_text_channel(
                name=channel_name, overwrites=overwrites, category=category,
            )
        except discord.HTTPException as e:
            err_msg = str(e).lower()
            if "category" in err_msg or "parent" in err_msg or "50035" in str(e):
                await interaction.response.send_message(t("err_category", author_lang), ephemeral=True)
            else:
                await interaction.response.send_message(f"❌ Erro ao criar ticket: {e}", ephemeral=True)
            return

        ticket_data = {
            "guild_id": str(guild.id), "channel_id": str(ticket_channel.id),
            "author_id": str(user.id), "staff_id": None, "ticket_code": ticket_code,
            "status": "OPEN", "created_at": datetime.utcnow().isoformat(),
            "server_id": server_id, "server_name": server_name or "N/A",
            "nick": nick, "steam_id": steam_id, "reason": (reason or "").strip(),
            "lang": lang,
            "author_lang": author_lang,
            "message_translations": {},
            "last_translated_message_id": None,
        }
        add_ticket(str(guild.id), ticket_data)

        banner = config.get("ticket_banner")
        embed_title = t("ticket_title", author_lang)
        embed_desc = (
            f"{t('ticket_desc', author_lang)}\n\n"
            f"🆔 **{t('protocol', author_lang)}:** `{ticket_code}`\n"
            f"🖥️ **{t('server', author_lang)}:** {server_name or 'N/A'}\n"
            f"🎮 **{t('nick_in_game', author_lang)}:** {nick}\n"
            f"📌 **Steam ID:** {steam_id}"
        )

        embed = discord.Embed(
            title=embed_title,
            description=embed_desc,
            color=color_from_hex(config.get("color", "#5865F2")),
        )
        if banner:
            embed.set_image(url=banner)
        embed.set_thumbnail(url=guild.icon.url if guild.icon else None)
        embed.set_footer(text="Suporte Valley • Ticket System")

        view = TicketView(self.bot)
        content = f"<@&{support_role_id}>" if support_role_id else None
        ticket_msg = await ticket_channel.send(content=content, embed=embed, view=view)
        update_ticket(str(ticket_channel.id), {"ticket_message_id": ticket_msg.id})

        # Painel staff: embed fixo (título/desc no idioma do jogador)
        staff_panel_embed = discord.Embed(
            title=f"📋 {t('panel_staff_title', author_lang)}",
            description=f"*{t('panel_staff_waiting', author_lang)}*",
            color=0x95A5A6,
        )
        staff_panel_embed.add_field(name=t("panel_ticket_lang", author_lang), value=author_lang.upper(), inline=True)
        staff_panel_msg = await ticket_channel.send(embed=staff_panel_embed)
        update_ticket(str(ticket_channel.id), {"staff_panel_message_id": staff_panel_msg.id})

        author_mention = user.mention
        ask_reason_msg = f"👋 {author_mention} — **{t('ask_reason', author_lang)}**"
        await ticket_channel.send(ask_reason_msg)

        confirm_msg = f"✅ **{t('confirm_created', author_lang)}:** {ticket_channel.mention}"
        await interaction.response.send_message(confirm_msg, ephemeral=True)

    async def _do_notify_dm(self, interaction: discord.Interaction):
        """Notifica o autor do ticket no privado (só staff). Mensagem e botão no idioma do ticket (pt/en)."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message(f"❌ {t('staff_only', ticket.get('author_lang') or 'en')}", ephemeral=True)
        author_id = ticket.get("author_id")
        try:
            author = await self.bot.fetch_user(int(author_id))
        except (discord.NotFound, ValueError):
            return await interaction.response.send_message("❌ Não foi possível encontrar o usuário.", ephemeral=True)
        author_lang = ticket.get("author_lang") or "en"
        channel_link = f"https://discord.com/channels/{interaction.guild.id}/{interaction.channel.id}"
        title = f"📩 {t('notify_dm_title', author_lang)}"
        description = t("notify_dm_desc", author_lang)
        btn_label = t("open_ticket", author_lang)
        embed = discord.Embed(title=title, description=description, color=0x5865F2)
        embed.set_footer(text=interaction.guild.name)
        view = discord.ui.View()
        view.add_item(discord.ui.Button(label=btn_label, style=discord.ButtonStyle.link, url=channel_link, emoji="🎫"))
        try:
            await author.send(embed=embed, view=view)
        except discord.Forbidden:
            await interaction.response.send_message("❌ Não foi possível enviar DM (usuário pode ter DMs desativadas).", ephemeral=True)
            return
        await interaction.response.send_message("✅ Member notified in DMs.", ephemeral=True)

    async def _do_translations_menu(self, interaction: discord.Interaction):
        """Menu com 3 opções: última mensagem, últimas 5, conversa completa. Resposta via ephemeral."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message("❌ Apenas a equipe de suporte pode usar este botão.", ephemeral=True)

        view = discord.ui.View(timeout=60)
        opts = [
            (" Última mensagem (PT)", "sv_trans_last"),
            (" Últimas 5 mensagens (PT)", "sv_trans_5"),
            (" Conversa completa (PT)", "sv_trans_full"),
        ]
        for label, cid in opts:
            btn = discord.ui.Button(label=label, style=discord.ButtonStyle.secondary, emoji="🌐", custom_id=cid)
            btn.callback = lambda i, c=cid: self._on_translation_option(i, c)
            view.add_item(btn)
        await interaction.response.send_message("Escolha o tipo de tradução (resposta só você vê):", view=view, ephemeral=True)

    async def _on_translation_option(self, interaction: discord.Interaction, option: str):
        """Processa clique em última / 5 últimas / completa."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket não encontrado.", ephemeral=True)
        trans = ticket.get("message_translations") or {}
        if not trans:
            return await interaction.response.send_message("Nenhuma mensagem traduzida neste ticket.", ephemeral=True)

        items = [(int(k), v) for k, v in trans.items()]
        items.sort(key=lambda x: x[0])
        author_lang = ticket.get("author_lang") or "en"

        if option == "sv_trans_last":
            last = items[-1] if items else None
            if not last:
                return await interaction.response.send_message("Nenhuma mensagem traduzida.", ephemeral=True)
            _, v = last
            who = "Jogador" if v.get("is_player") else "Staff"
            text = f"**{who}:** {v.get('translated', v.get('content', ''))}"
            await interaction.response.send_message(text[:1900], ephemeral=True)
            return

        if option == "sv_trans_5":
            last5 = items[-5:]
            lines = []
            for _, v in last5:
                who = "Jogador" if v.get("is_player") else "Staff"
                lines.append(f"**{who}:** {v.get('translated', v.get('content', ''))}")
            full = "\n\n".join(lines)
            await interaction.response.send_message(full[:1900] if len(full) <= 1900 else full[:1900] + "\n...", ephemeral=True)
            return

        if option == "sv_trans_full":
            await interaction.response.defer(ephemeral=True)
            lines = []
            for _, v in items:
                who = "Jogador" if v.get("is_player") else "Staff"
                lines.append(f"**{who}:** {v.get('translated', v.get('content', ''))}")
            full = "\n\n".join(lines)
            chunk_size = 1900
            for i in range(0, len(full), chunk_size):
                chunk = full[i : i + chunk_size]
                await interaction.followup.send(chunk, ephemeral=True)
            await interaction.followup.send("✅ Conversa completa em PT.", ephemeral=True)

    async def _do_change_lang(self, interaction: discord.Interaction):
        """Altera idioma do ticket manualmente (só staff, com log)."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message("❌ Apenas a equipe de suporte pode usar este botão.", ephemeral=True)

        options = [discord.SelectOption(label=f"{emoji} {label}", value=str(code)) for code, emoji, label in get_lang_options_for_select()]
        select = discord.ui.Select(
            placeholder="Novo idioma do ticket",
            options=options[:25],
            custom_id="sv_change_lang_select",
        )

        async def on_select(callback_interaction: discord.Interaction):
            if callback_interaction.data and callback_interaction.data.get("values"):
                new_lang = callback_interaction.data["values"][0]
                old_lang = ticket.get("author_lang") or "en"
                log_entries = list(ticket.get("translation_log") or [])
                log_entries.append({
                    "ts": datetime.utcnow().isoformat(),
                    "event": "lang_change",
                    "from": old_lang,
                    "to": new_lang,
                    "by": str(callback_interaction.user.id),
                })
                if len(log_entries) > 50:
                    log_entries = log_entries[-50:]
                update_ticket(str(interaction.channel.id), {"author_lang": new_lang, "translation_log": log_entries})
                await callback_interaction.response.send_message(
                    f"✅ Idioma alterado de {old_lang.upper()} para {new_lang.upper()} (registrado no ticket).",
                    ephemeral=True,
                )

        select.callback = on_select
        view = discord.ui.View(timeout=60)
        view.add_item(select)
        await interaction.response.send_message("Selecione o novo idioma (alteração manual — registrada no ticket):", view=view, ephemeral=True)

    async def _do_toggle_autoclose(self, interaction: discord.Interaction):
        """Ativa/desativa auto-fechamento por inatividade para este ticket (só staff)."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            return await interaction.response.send_message(
                "❌ Apenas a equipe de suporte pode usar este botão.", ephemeral=True
            )
        current = bool(ticket.get("auto_close_disabled", False))
        new_val = not current
        update_ticket(str(interaction.channel.id), {"auto_close_disabled": new_val})
        if new_val:
            msg = "✅ Auto-fechamento por inatividade **desativado** para este ticket."
        else:
            msg = "✅ Auto-fechamento por inatividade **ativado** para este ticket."
        await interaction.response.send_message(msg, ephemeral=True)

    async def _handle_transfer_click(self, interaction: discord.Interaction):
        """Abre o select para o staff escolher outro suporte (só staff). Texto no idioma do ticket."""
        ticket = get_ticket_by_channel(str(interaction.channel.id))
        if not ticket:
            return await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
        config = get_guild_config(str(interaction.guild.id))
        if not _is_staff(interaction.user, config):
            msg = "❌ Only support staff can transfer this ticket." if ticket.get("lang") == "en" else "❌ Apenas a equipe de suporte pode transferir o ticket."
            return await interaction.response.send_message(msg, ephemeral=True)
        lang = ticket.get("lang", "pt")
        view = TransferSelectView(
            self.bot,
            str(interaction.guild.id),
            str(interaction.channel.id),
            interaction.message.id,
            lang=author_lang,
        )
        prompt = f"**{t('transfer_prompt', author_lang)}**"
        await interaction.response.send_message(prompt, view=view, ephemeral=True)

    async def _do_transfer_ticket(self, interaction: discord.Interaction, new_staff: discord.Member, channel_id: str, ticket_message_id: int):
        """Transfere o ticket para outro staff: atualiza staff_id, mensagem no canal e view."""
        ticket = get_ticket_by_channel(channel_id)
        if not ticket:
            await interaction.response.send_message("❌ Ticket not found.", ephemeral=True)
            return
        author_id = ticket.get("author_id")
        update_ticket(channel_id, {"staff_id": str(new_staff.id)})
        channel = interaction.guild.get_channel(int(channel_id))
        author_mention = f"<@{author_id}>" if author_id else ""
        author_lang = ticket.get("author_lang") or "en"
        msg_channel = f"✅ {t('transfer_channel', author_lang, mention=author_mention, staff=new_staff.display_name)}"
        msg_ephemeral = f"✅ {t('transfer_dm_title', author_lang)} → **{new_staff.display_name}**"
        if channel:
            await channel.send(msg_channel)
        try:
            ticket_msg = await channel.fetch_message(ticket_message_id)
            new_view = _build_claimed_view(self.bot, new_staff)
            await ticket_msg.edit(view=new_view)
        except (discord.NotFound, discord.Forbidden):
            pass
        try:
            author = await self.bot.fetch_user(int(author_id))
            channel_link = f"https://discord.com/channels/{interaction.guild.id}/{int(channel_id)}"
            dm_text = t("transfer_dm_desc", author_lang, staff=new_staff.display_name)
            dm_btn_label = t("open_ticket", author_lang)
            dm_view = discord.ui.View()
            dm_view.add_item(
                discord.ui.Button(
                    label=dm_btn_label,
                    style=discord.ButtonStyle.link,
                    url=channel_link,
                    emoji="🎫",
                )
            )
            await author.send(dm_text, view=dm_view)
        except (discord.Forbidden, discord.NotFound, ValueError):
            pass
        # DM para o staff que recebeu a transferência (com link para o ticket) — sempre em PT-BR
        channel_link = f"https://discord.com/channels/{interaction.guild.id}/{int(channel_id)}"
        staff_dm_title = "📩 Ticket transferido para você"
        staff_dm_desc = f"Um ticket foi transferido para você no servidor **{interaction.guild.name}**. Clique no botão abaixo para abrir o ticket."
        staff_btn_label = "Abrir ticket"
        staff_embed = discord.Embed(title=staff_dm_title, description=staff_dm_desc, color=0x5865F2)
        staff_view = discord.ui.View()
        staff_view.add_item(discord.ui.Button(label=staff_btn_label, style=discord.ButtonStyle.link, url=channel_link, emoji="🎫"))
        try:
            await new_staff.send(embed=staff_embed, view=staff_view)
        except (discord.Forbidden, discord.NotFound):
            pass
        await interaction.response.edit_message(content=msg_ephemeral, view=None)

    def _build_config_embed(self, guild_id: str) -> discord.Embed:
        """Constrói o embed de configuração."""
        config = get_guild_config(guild_id)
        cat = config.get("category_id")
        logs = config.get("logs_channel_id")
        role = config.get("support_role_id")
        servers = config.get("servers", [])

        embed = discord.Embed(
            title="⚙️ Configuração de Tickets",
            description="Use os **Select Menus** e **Botões** abaixo.\n"
            f"📩 **Transcript DM (HTML):** ✅ | 📄 **Canal transcripts:** {'✅' if config.get('transcript_channel_id') else '❌'} | 🖥️ **Servidores:** {len(servers)}",
            color=color_from_hex(config.get("color", "#5865F2")),
            timestamp=datetime.utcnow(),
        )
        transcript_ch = config.get("transcript_channel_id")
        cat_pt = config.get("category_id_pt") or cat
        cat_en = config.get("category_id_en") or cat
        embed.add_field(name="📁 Categoria PT-BR", value=f"<#{cat_pt}>" if cat_pt else "`Não definido`", inline=True)
        embed.add_field(name="📁 Categoria US/EN", value=f"<#{cat_en}>" if cat_en else "`Não definido`", inline=True)
        embed.add_field(name="📋 Logs", value=f"<#{logs}>" if logs else "`Não definido`", inline=True)
        embed.add_field(name="📄 Canal Transcripts", value=f"<#{transcript_ch}>" if transcript_ch else "`Não definido`", inline=True)
        embed.add_field(name="🛡️ Cargo Suporte", value=f"<@&{role}>" if role else "`Não definido`", inline=True)
        embed.add_field(
            name="🖥️ Servidores (lista para seleção)",
            value="\n".join(f"• {s.get('name','?')}" for s in servers[:10]) or "`Nenhum`",
            inline=False,
        )
        embed.add_field(
            name="🎨 Painel (sala)",
            value=f"**PT:** {config.get('panel_title_pt','')[:25]}... | **Banner:** {'✅' if config.get('panel_banner') else '❌'}",
            inline=True,
        )
        embed.add_field(
            name="📄 Embed do Ticket",
            value=f"**PT:** {config.get('ticket_title_pt','')[:25]}... | **Banner:** {'✅' if config.get('ticket_banner') else '❌'}",
            inline=True,
        )
        cd = config.get("ticket_close_delay_seconds", 60)
        ua = config.get("ticket_unanswered_alert_minutes", 240)
        sr = config.get("ticket_staff_reminder_minutes", 60)
        ai = config.get("ticket_author_inactivity_close_minutes", 480)
        embed.add_field(
            name="⏱️ Tempos de ticket",
            value=f"Fechar: **{cd}s** | Alerta sem resposta: **{ua} min** | Lembrete staff: **{sr} min** | Auto-fechar inatividade: **{ai} min**",
            inline=False,
        )
        embed.set_footer(text="Suporte Valley • Sistema de Configuração")
        return embed

    async def _show_ticket_config(self, interaction: discord.Interaction):
        """Mostra a config de tickets (categoria Ticket)."""
        embed = self._build_config_embed(str(interaction.guild.id))
        view = ConfigEmbedView(self.bot, str(interaction.guild.id), self._build_config_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def _show_config_bot(self, interaction: discord.Interaction):
        """Mostra a config do bot (categoria Config Bot)."""
        embed = self._build_config_bot_embed(str(interaction.guild.id))
        view = ConfigBotView(self.bot, str(interaction.guild.id), self._build_config_bot_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    def _build_logs_embed(self, guild_id: str) -> discord.Embed:
        """Embed da categoria Logs — canal de status do bot."""
        config = get_guild_config(guild_id)
        bot_log = config.get("bot_log_channel_id")
        embed = discord.Embed(
            title="📋 Logs do Bot",
            description="Configure o canal onde o bot envia o **status de startup** (ao iniciar).",
            color=0x5865F2,
            timestamp=datetime.utcnow(),
        )
        embed.add_field(
            name="📢 Canal de Log do Bot",
            value=f"✅ <#{bot_log}>" if bot_log else "`Não definido` — Selecione abaixo",
            inline=True,
        )
        embed.set_footer(text="Mensagens de startup: status, configurações e erros")
        return embed

    def _build_config_bot_embed(self, guild_id: str) -> discord.Embed:
        """Constrói o embed de Config Bot."""
        config = get_guild_config(guild_id)
        allowed = config.get("allowed_sup_users", [])
        support_role = config.get("support_role_id")

        embed = discord.Embed(
            title="🤖 Config Bot",
            description="Configurações do bot. **Apenas o dono** pode adicionar/remover usuários autorizados.",
            color=0x5865F2,
            timestamp=datetime.utcnow(),
        )
        embed.add_field(
            name="👑 Dono do Bot",
            value=f"<@{BOT_OWNER_ID}>",
            inline=True,
        )
        embed.add_field(
            name="✅ Usuários autorizados (!sup)",
            value="\n".join(f"• <@{uid}>" for uid in allowed[:10]) or "`Nenhum`",
            inline=True,
        )
        embed.add_field(
            name="🛡️ Cargo de Suporte",
            value=f"<@&{support_role}>" if support_role else "`Não definido`",
            inline=True,
        )
        maintenance = config.get("maintenance_mode", False)
        embed.add_field(
            name="🔧 Modo Manutenção",
            value="⚠️ **ATIVO** — Tickets bloqueados" if maintenance else "✅ **Inativo**",
            inline=False,
        )
        embed.set_footer(text="Somente o dono pode alterar estas configurações")

        return embed


class SupMainView(discord.ui.View):
    """Menu principal com categorias: Ticket | Config Bot."""

    def __init__(self, bot, guild_id: str):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id

    @discord.ui.select(
        placeholder="📂 Selecione uma categoria...",
        options=[
            discord.SelectOption(label="Ticket", value="ticket", emoji="🎫", description="Categoria, logs, painel e aparência"),
            discord.SelectOption(label="Config Bot", value="config_bot", emoji="🤖", description="Permissões e cargo de suporte"),
            discord.SelectOption(label="Agente", value="agent", emoji="🤖", description="Supervisão, treino e ações"),
            discord.SelectOption(label="Logs", value="logs", emoji="📋", description="Canal de status do bot (startup)"),
            discord.SelectOption(label="⏱️ Tempos de ticket", value="timers", emoji="⏱️", description="Delay fechamento, alertas e auto-fechar"),
        ],
        row=0,
    )
    async def select_category(self, interaction: discord.Interaction, select: discord.ui.Select):
        value = select.values[0] if select.values else ""
        if value == "ticket":
            cog = interaction.client.get_cog("TicketCog")
            if cog:
                embed = cog._build_config_embed(self.guild_id)
                view = ConfigEmbedView(self.bot, self.guild_id, cog._build_config_embed)
                await interaction.response.edit_message(embed=embed, view=view)
        elif value == "config_bot":
            cog = interaction.client.get_cog("TicketCog")
            if cog:
                embed = cog._build_config_bot_embed(self.guild_id)
                view = ConfigBotView(self.bot, self.guild_id, cog._build_config_bot_embed)
                await interaction.response.edit_message(embed=embed, view=view)
        elif value == "agent":
            cog = interaction.client.get_cog("AgentCog")
            if cog:
                from cogs.agent import AgentConfigView
                embed = cog._build_agent_embed(self.guild_id)
                view = AgentConfigView(self.bot, self.guild_id, cog._build_agent_embed)
                await interaction.response.edit_message(embed=embed, view=view)
        elif value == "logs":
            cog = interaction.client.get_cog("TicketCog")
            if cog:
                embed = cog._build_logs_embed(self.guild_id)
                view = LogsConfigView(self.bot, self.guild_id, cog._build_logs_embed)
                await interaction.response.edit_message(embed=embed, view=view)
        elif value == "timers":
            modal = TicketTimersModal(self.guild_id)
            await interaction.response.send_modal(modal)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class ConfigBotView(discord.ui.View):
    """Config do bot: dono, usuários autorizados, cargo suporte, modo manutenção."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func
        config = get_guild_config(guild_id)
        maintenance = config.get("maintenance_mode", False)
        btn_maint_on = discord.ui.Button(
            label="🔧 Entrar em Manutenção",
            style=discord.ButtonStyle.danger,
            custom_id="sv_maint_on",
            row=3,
            disabled=maintenance,
        )
        btn_maint_off = discord.ui.Button(
            label="✅ Sair da Manutenção",
            style=discord.ButtonStyle.success,
            custom_id="sv_maint_off",
            row=3,
            disabled=not maintenance,
        )
        btn_maint_on.callback = self._maint_on_callback
        btn_maint_off.callback = self._maint_off_callback
        self.add_item(btn_maint_on)
        self.add_item(btn_maint_off)

    async def _maint_on_callback(self, interaction: discord.Interaction):
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            return await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
        cog = interaction.client.get_cog("TicketCog")
        if not cog:
            return await interaction.response.send_message("❌ Erro ao acessar módulo de tickets.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        count = await cog._enter_maintenance_mode(self.guild_id)
        await interaction.followup.send(
            f"✅ **Modo manutenção ativado.**\n{'📋 ' + str(count) + ' ticket(s) sendo fechado(s) automaticamente.' if count else 'Nenhum ticket aberto.'}",
            ephemeral=True,
        )
        try:
            await interaction.message.edit(
                embed=self.build_embed(self.guild_id),
                view=ConfigBotView(self.bot, self.guild_id, self.build_embed),
            )
        except discord.NotFound:
            pass

    async def _maint_off_callback(self, interaction: discord.Interaction):
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            return await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
        cog = interaction.client.get_cog("TicketCog")
        if not cog:
            return await interaction.response.send_message("❌ Erro ao acessar módulo de tickets.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        await cog._exit_maintenance_mode(self.guild_id)
        try:
            await interaction.message.edit(
                embed=self.build_embed(self.guild_id),
                view=ConfigBotView(self.bot, self.guild_id, self.build_embed),
            )
        except discord.NotFound:
            pass
        await interaction.followup.send("✅ **Modo manutenção desativado.** Os jogadores podem abrir tickets novamente.", ephemeral=True)

    @discord.ui.select(
        cls=UserSelect,
        custom_id="sv_bot_add_user",
        placeholder="➕ Adicionar usuário ao !sup (apenas dono)",
        max_values=1,
        row=0,
    )
    async def add_user(self, interaction: discord.Interaction, select: UserSelect):
        if not is_bot_owner(str(interaction.user.id)):
            return await interaction.response.send_message("❌ Apenas o **dono do bot** pode adicionar usuários.", ephemeral=True)
        user = select.values[0] if select.values else None
        if not user:
            return
        config = get_guild_config(self.guild_id)
        uid = str(user.id)
        allowed = config.get("allowed_sup_users", [])
        if uid in allowed:
            pass
        elif len(allowed) >= MAX_ALLOWED_SUP_USERS:
            return await interaction.response.send_message(
                f"⚠️ Limite de {MAX_ALLOWED_SUP_USERS} usuários atingido.", ephemeral=True
            )
        else:
            config["allowed_sup_users"] = allowed + [uid]
            save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigBotView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ **{user}** autorizado a usar `!sup`", ephemeral=True)

    @discord.ui.select(
        cls=RoleSelect,
        custom_id="sv_bot_support_role",
        placeholder="🛡️ Definir cargo de suporte para tickets",
        max_values=1,
        row=1,
    )
    async def set_support_role(self, interaction: discord.Interaction, select: RoleSelect):
        if not is_bot_owner(str(interaction.user.id)):
            return await interaction.response.send_message("❌ Apenas o **dono do bot** pode definir o cargo de suporte.", ephemeral=True)
        role = select.values[0] if select.values else None
        if not role:
            return
        config = get_guild_config(self.guild_id)
        config["support_role_id"] = str(role.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigBotView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Cargo de suporte: {role.mention}", ephemeral=True)

    @discord.ui.button(label="Remover usuário", emoji="➖", style=discord.ButtonStyle.danger, custom_id="sv_bot_remove", row=2)
    async def remove_user_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not is_bot_owner(str(interaction.user.id)):
            return await interaction.response.send_message("❌ Apenas o **dono do bot** pode remover usuários.", ephemeral=True)
        config = get_guild_config(self.guild_id)
        allowed = config.get("allowed_sup_users", [])
        if not allowed:
            return await interaction.response.send_message("❌ Nenhum usuário para remover.", ephemeral=True)

        modal = RemoveUserModal(self.guild_id, allowed)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="sv_bot_back", row=2)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        view = SupMainView(self.bot, self.guild_id)
        await interaction.response.edit_message(embed=_main_embed(), view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


def _main_embed() -> discord.Embed:
    """Embed do menu principal."""
    embed = discord.Embed(
        title="⚙️ Suporte Valley — Painel Central",
        description="Selecione uma **categoria** abaixo:",
        color=0x5865F2,
        timestamp=datetime.utcnow(),
    )
    embed.add_field(name="🎫 Ticket", value="Categoria, logs, painel e aparência", inline=True)
    embed.add_field(name="🤖 Config Bot", value="Permissões e cargo de suporte", inline=True)
    embed.add_field(name="🤖 Agente", value="Supervisão, treino e ações", inline=True)
    embed.add_field(name="📋 Logs", value="Canal de status do bot (startup)", inline=True)
    embed.set_footer(text="Use o menu abaixo")
    return embed


class LogsConfigView(discord.ui.View):
    """Config da categoria Logs — canal de startup do bot."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="sv_logs_bot_channel",
        placeholder="📢 Selecione o canal de log do bot (startup)",
        row=0,
    )
    async def select_bot_log(self, interaction: discord.Interaction, select: ChannelSelect):
        channel = select.values[0] if select.values else None
        if not channel:
            return
        config = get_guild_config(self.guild_id)
        config["bot_log_channel_id"] = str(channel.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=LogsConfigView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Canal de log do bot: {channel.mention}", ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="sv_logs_back", row=1)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        view = SupMainView(self.bot, self.guild_id)
        await interaction.response.edit_message(embed=_main_embed(), view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class PreTicketLanguageSelectView(discord.ui.View):
    """OBRIGATÓRIO: seleção de idioma do jogador antes de criar o ticket. Idioma ficará FIXO até fechar."""

    def __init__(self, bot, guild_id: str, config: dict, lang: str,
                 server_id: str = "", server_name: str = "N/A", category_id: str | None = None):
        super().__init__(timeout=120)
        self.bot = bot
        self.guild_id = guild_id
        self.config = config
        self._lang = lang
        self._server_id = server_id
        self._server_name = server_name
        self._category_id = category_id or _get_category_for_lang(config, lang)
        options = [
            discord.SelectOption(label=label, value=str(code), emoji=emoji)
            for code, emoji, label in get_lang_options_for_select()
        ]
        select = discord.ui.Select(
            placeholder="Selecione o idioma / Select language" if lang == "pt" else "Select your language",
            options=options[:25],
            custom_id="sv_preticket_lang",
            row=0,
        )
        select.callback = self._on_select_lang
        self.add_item(select)

    async def _on_select_lang(self, interaction: discord.Interaction):
        values = interaction.data.get("values") if interaction.data else []
        if not values:
            msg = "❌ Selecione um idioma." if self._lang == "pt" else "❌ Select a language."
            return await interaction.response.send_message(msg, ephemeral=True)
        author_lang = values[0]
        modal = PreTicketModal(
            self.bot, self.guild_id, [], self._category_id,
            interaction.guild.id, interaction.user.id, lang=self._lang or "pt",
            preselected_server_id=self._server_id,
            preselected_server_name=self._server_name,
            preselected_category_id=self._category_id or "",
            author_lang=author_lang,
        )
        await interaction.response.send_modal(modal)


class PreTicketServerView(discord.ui.View):
    """View com Select de servidores. Ao selecionar, exibe seleção OBRIGATÓRIA de idioma."""

    def __init__(self, bot, guild_id: str, config: dict, lang: str):
        super().__init__(timeout=120)
        self.bot = bot
        self.guild_id = guild_id
        self.config = config
        self._lang = lang
        servers = config.get("servers", [])
        if servers:
            options = [
                discord.SelectOption(label=s.get("name", "?"), value=str(s.get("id", "")), emoji="🖥️")
                for s in servers[:25]
            ]
            select = discord.ui.Select(
                placeholder="Selecione o servidor..." if lang == "pt" else "Select the server...",
                options=options,
                custom_id="sv_preticket_server",
                row=0,
            )
            select.callback = self._on_select_server
            self.add_item(select)

    async def _on_select_server(self, interaction: discord.Interaction, select: discord.ui.Select | None = None):
        """Ao selecionar servidor, exibe menu OBRIGATÓRIO de idioma antes do modal."""
        values = (select.values if select else None) or (interaction.data.get("values") if interaction.data else [])
        if not values:
            msg = "❌ Selecione um servidor válido." if self._lang == "pt" else "❌ Select a valid server."
            return await interaction.response.send_message(msg, ephemeral=True)

        server_id = values[0]
        servers = self.config.get("servers", [])
        server = next((s for s in servers if str(s.get("id")) == str(server_id)), None)
        if not server:
            msg = "❌ Servidor inválido." if self._lang == "pt" else "❌ Invalid server."
            return await interaction.response.send_message(msg, ephemeral=True)

        category_fallback = _get_category_for_lang(self.config, self._lang or "pt")
        view = PreTicketLanguageSelectView(
            self.bot, self.guild_id, self.config, self._lang or "pt",
            server_id=str(server.get("id", "")),
            server_name=server.get("name") or "N/A",
            category_id=category_fallback,
        )
        intro = "Selecione o idioma do atendimento abaixo:" if self._lang == "pt" else "Select your language for this ticket:"
        await interaction.response.edit_message(content=intro, view=view)


class PreTicketModal(Modal):
    """Modal: nick e Steam ID. Servidor e autor_lang vêm do Select. Motivo é perguntado pela IA no canal após abrir."""

    def __init__(self, bot, guild_id: str, servers: list, category_id: str | None, guild_id_int: int, user_id_int: int, lang: str = "pt",
                 preselected_server_id: str | None = None, preselected_server_name: str | None = None, preselected_category_id: str | None = None,
                 author_lang: str = "en"):
        super().__init__(timeout=120, title=t("modal_title", author_lang))
        self.bot = bot
        self.guild_id = guild_id
        self.servers = servers or []
        self.category_id = category_id
        self._guild_id_int = guild_id_int
        self._user_id_int = user_id_int
        self._lang = lang
        self._author_lang = author_lang
        self._preselected = (preselected_server_id, preselected_server_name or "N/A", preselected_category_id or "")

        # Só pede servidor por texto se não tiver servidores pré-setados e não veio servidor selecionado
        if self.servers and not preselected_server_id:
            server_names = ", ".join((s.get("name") or "?") for s in self.servers[:10])
            placeholder = f"Ex: {server_names}"[:100]
            self.server_input = TextInput(
                label=t("server", author_lang),
                placeholder=placeholder,
                required=True,
                max_length=50,
            )
            self.add_item(self.server_input)
        else:
            self.server_input = None

        self.nick_input = TextInput(
            label=t("nick_label", author_lang),
            placeholder=t("nick_placeholder", author_lang),
            required=True,
            max_length=50,
        )
        self.steam_input = TextInput(
            label=t("steam_label", author_lang),
            placeholder=t("steam_placeholder", author_lang),
            required=True,
            max_length=30,
        )
        self.add_item(self.nick_input)
        self.add_item(self.steam_input)

    def _resolve_server(self, name: str) -> tuple[str | None, str | None, str | None]:
        """Retorna (server_id, server_name, category_id) ou (None, None, None) se não achar."""
        name = (name or "").strip()
        for s in self.servers:
            if (s.get("name") or "").strip().lower() == name.lower():
                return str(s.get("id", "")), (s.get("name") or "N/A"), ""
        return None, None, None

    async def on_submit(self, interaction: discord.Interaction):
        cog = interaction.client.get_cog("TicketCog")
        if not cog:
            return

        if self._preselected[0]:
            server_id, server_name, category_id = self._preselected[0], self._preselected[1], self._preselected[2] or self.category_id
        elif self.server_input:
            raw = self.server_input.value.strip()
            sid, sname, cid = self._resolve_server(raw)
            if sid is None:
                return await interaction.response.send_message(f"❌ {t('invalid_server', self._author_lang)}", ephemeral=True)
            server_id, server_name, category_id = sid, sname, cid
        else:
            server_id, server_name, category_id = "", "N/A", self.category_id

        steam_raw = self.steam_input.value.strip()
        valid, steam_id = is_valid_steam_id64(steam_raw)
        if not valid:
            return await interaction.response.send_message(f"❌ {t('invalid_steam_id', self._author_lang)}", ephemeral=True)

        await cog._create_ticket(
            interaction, interaction.user,
            server_id, server_name, category_id,
            self.nick_input.value.strip(),
            steam_id,
            reason="",
            lang=self._lang,
            author_lang=self._author_lang,
        )


class AddServerModal(Modal, title="Adicionar Servidor"):
    """Modal para nome do servidor (apenas coleta info, usa categorias PT/EN padrão)."""

    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.name_input = TextInput(label="Nome do servidor", placeholder="Ex: EU1", required=True, max_length=30)
        self.add_item(self.name_input)

    async def on_submit(self, interaction: discord.Interaction):
        name = self.name_input.value.strip()
        await interaction.response.defer(ephemeral=True)

        config = get_guild_config(self.guild_id)
        servers = config.get("servers", [])
        if len(servers) >= MAX_SERVERS:
            return await interaction.followup.send(
                f"⚠️ Limite de {MAX_SERVERS} servidores atingido.", ephemeral=True
            )
        ids = [int(s.get("id", 0)) for s in servers if str(s.get("id", "0")).isdigit()]
        new_id = str(max(ids + [0]) + 1)
        servers.append({"id": new_id, "name": name})
        config["servers"] = servers[-MAX_SERVERS:]
        save_guild_config(self.guild_id, config)

        await interaction.followup.send(f"✅ Servidor **{name}** adicionado à lista.", ephemeral=True)


class EditServerModal(Modal, title="Editar Servidor"):
    """Modal para editar o nome de um servidor na lista."""

    def __init__(self, guild_id: str, server_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.server_id = server_id
        config = get_guild_config(self.guild_id)
        servers = config.get("servers", [])
        current = next((s for s in servers if str(s.get("id")) == str(server_id)), {})
        current_name = current.get("name", "")
        self.name_input = TextInput(
            label="Nome do servidor",
            placeholder="Ex: EU1",
            default=current_name,
            required=True,
            max_length=30,
        )
        self.add_item(self.name_input)

    async def on_submit(self, interaction: discord.Interaction):
        name = self.name_input.value.strip()
        config = get_guild_config(self.guild_id)
        servers = config.get("servers", [])
        changed = False
        for s in servers:
            if str(s.get("id")) == str(self.server_id):
                s["name"] = name
                changed = True
                break
        if changed:
            config["servers"] = servers
            save_guild_config(self.guild_id, config)
            await interaction.response.send_message(f"✅ Servidor atualizado para **{name}**.", ephemeral=True)
        else:
            await interaction.response.send_message("❌ Servidor não encontrado na configuração.", ephemeral=True)


class PanelEmbedModal(Modal, title="Editar Embed"):
    """Modal para título, descrição e cor PT/EN. Banner é configurado em botão separado."""

    def __init__(self, guild_id: str, typ: str, config: dict):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.typ = typ
        prefix = "panel" if typ == "panel" else "ticket"
        color_val = config.get("color", "#5865F2")
        self.title_pt = TextInput(label="Título PT-BR", default=config.get(f"{prefix}_title_pt", ""), max_length=100)
        self.title_en = TextInput(label="Título EN-US", default=config.get(f"{prefix}_title_en", ""), max_length=100)
        self.desc_pt = TextInput(label="Descrição PT-BR", default=config.get(f"{prefix}_desc_pt", ""), style=discord.TextStyle.paragraph, max_length=500)
        self.desc_en = TextInput(label="Descrição EN-US", default=config.get(f"{prefix}_desc_en", ""), style=discord.TextStyle.paragraph, max_length=500)
        self.color_input = TextInput(
            label="Cor do embed (hex)",
            default=color_val,
            required=False,
            max_length=10,
            placeholder="#5865F2",
        )
        for w in [self.title_pt, self.title_en, self.desc_pt, self.desc_en, self.color_input]:
            self.add_item(w)

    async def on_submit(self, interaction: discord.Interaction):
        config = get_guild_config(self.guild_id)
        prefix = "panel" if self.typ == "panel" else "ticket"
        config[f"{prefix}_title_pt"] = self.title_pt.value.strip()
        config[f"{prefix}_title_en"] = self.title_en.value.strip()
        config[f"{prefix}_desc_pt"] = self.desc_pt.value.strip()
        config[f"{prefix}_desc_en"] = self.desc_en.value.strip()

        raw_color = (self.color_input.value or "").strip()
        if raw_color:
            if raw_color.startswith("#"):
                color = raw_color
            elif len(raw_color) == 6 and raw_color.isalnum():
                color = f"#{raw_color}"
            else:
                color = None
            if color:
                config["color"] = color

        save_guild_config(self.guild_id, config)
        await interaction.response.send_message(f"✅ Embed do {'painel' if self.typ == 'panel' else 'ticket'} atualizado!", ephemeral=True)


class PanelBannerModal(Modal, title="Editar Banner"):
    """Modal separado para configurar apenas o banner (imagem) do painel ou do embed do ticket."""

    def __init__(self, guild_id: str, typ: str, config: dict):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.typ = typ
        prefix = "panel" if typ == "panel" else "ticket"
        banner_val = config.get(f"{prefix}_banner") or ""
        self.banner_input = TextInput(
            label="Banner (URL da imagem)",
            default=banner_val,
            required=False,
            style=discord.TextStyle.paragraph,
            max_length=250,
            placeholder="Ex: https://.../imagem.png (deixe vazio para remover)",
        )
        self.add_item(self.banner_input)

    async def on_submit(self, interaction: discord.Interaction):
        config = get_guild_config(self.guild_id)
        prefix = "panel" if self.typ == "panel" else "ticket"
        raw = (self.banner_input.value or "").strip()
        if raw:
            config[f"{prefix}_banner"] = raw
        else:
            # Remove o banner se vazio
            config[f"{prefix}_banner"] = None
        save_guild_config(self.guild_id, config)
        await interaction.response.send_message(
            f"✅ Banner do {'painel' if self.typ == 'panel' else 'ticket'} atualizado!", ephemeral=True
        )


class PublishPanelChannelModal(Modal):
    """Modal para informar o ID do canal onde o painel será enviado."""

    def __init__(self, bot, guild_id: str, lang: str):
        title = "ID do canal" if lang == "pt" else "Channel ID"
        super().__init__(timeout=120, title=title)
        self.bot = bot
        self.guild_id = guild_id
        self.lang = lang
        self.channel_id_input = TextInput(
            label="ID do canal" if lang == "pt" else "Channel ID",
            placeholder="Ex: 1234567890123456789" if lang == "pt" else "e.g. 1234567890123456789",
            required=True,
            max_length=20,
        )
        self.add_item(self.channel_id_input)

    async def on_submit(self, interaction: discord.Interaction):
        raw = self.channel_id_input.value.strip().replace(" ", "")
        if not raw.isdigit():
            err = "❌ ID inválido. Use apenas números." if self.lang == "pt" else "❌ Invalid ID. Use numbers only."
            return await interaction.response.send_message(err, ephemeral=True)
        channel = interaction.guild.get_channel(int(raw))
        if not channel or not isinstance(channel, discord.TextChannel):
            err = "❌ Canal não encontrado ou não é um canal de texto. Verifique o ID." if self.lang == "pt" else "❌ Channel not found or not a text channel. Check the ID."
            return await interaction.response.send_message(err, ephemeral=True)
        config = get_guild_config(self.guild_id)
        title = config.get("panel_title_pt") if self.lang == "pt" else config.get("panel_title_en")
        desc = config.get("panel_desc_pt") if self.lang == "pt" else config.get("panel_desc_en")
        banner = config.get("panel_banner")
        embed = discord.Embed(title=title, description=desc, color=color_from_hex(config.get("color", "#5865F2")))
        if banner:
            embed.set_image(url=banner)
        view = OpenTicketView(self.bot, config, lang=self.lang)
        await channel.send(embed=embed, view=view)
        msg_ok = f"✅ Painel publicado em **PT-BR** em {channel.mention}!" if self.lang == "pt" else f"✅ Panel published in **EN** in {channel.mention}!"
        await interaction.response.send_message(msg_ok, ephemeral=True)


class PublishPanelLangView(discord.ui.View):
    """Seleção de idioma (PT-BR ou EN) e depois ID do canal para publicar o painel."""

    def __init__(self, bot, guild_id: str):
        super().__init__(timeout=60)
        self.bot = bot
        self.guild_id = guild_id

    @discord.ui.select(
        placeholder="Idioma do painel",
        options=[
            discord.SelectOption(label="Português (PT-BR)", value="pt", emoji="🇧🇷"),
            discord.SelectOption(label="English (EN-US)", value="en", emoji="🇺🇸"),
        ],
        custom_id="sv_publish_lang",
    )
    async def select_lang(self, interaction: discord.Interaction, select: discord.ui.Select):
        lang = select.values[0] if select.values else "pt"
        modal = PublishPanelChannelModal(self.bot, self.guild_id, lang)
        await interaction.response.send_modal(modal)


class RemoveUserModal(Modal, title="Remover usuário do !sup"):
    """Modal para informar o ID do usuário a remover."""

    def __init__(self, guild_id: str, allowed: list):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.allowed = allowed
        self.uid_input = TextInput(label="ID do usuário", placeholder="Ex: 123456789012345678", required=True, max_length=20)
        self.add_item(self.uid_input)

    async def on_submit(self, interaction: discord.Interaction):
        uid = self.uid_input.value.strip()
        if uid not in self.allowed:
            return await interaction.response.send_message(f"❌ ID `{uid}` não está na lista de autorizados.", ephemeral=True)
        config = get_guild_config(self.guild_id)
        config["allowed_sup_users"] = [x for x in config.get("allowed_sup_users", []) if x != uid]
        save_guild_config(self.guild_id, config)
        await interaction.response.send_message(f"✅ Usuário <@{uid}> removido do !sup.", ephemeral=True)


class TicketTimersModal(Modal, title="Tempos de ticket"):
    """Modal para configurar delay de fechamento, alerta sem resposta, lembrete staff e auto-fechamento por inatividade (use valores baixos para testar)."""

    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        config = get_guild_config(guild_id)
        self.close_delay = TextInput(
            label="Delay ao fechar (segundos)",
            placeholder="Ex: 60 (produção) ou 10 (teste)",
            default=str(config.get("ticket_close_delay_seconds", 60)),
            required=True,
            max_length=5,
        )
        self.unanswered = TextInput(
            label="Alerta suporte sem resposta (minutos)",
            placeholder="Ex: 240 (4h) ou 2 (teste)",
            default=str(config.get("ticket_unanswered_alert_minutes", 240)),
            required=True,
            max_length=5,
        )
        self.staff_reminder = TextInput(
            label="Lembrete staff a cada (minutos)",
            placeholder="Ex: 60 (1h) ou 1 (teste)",
            default=str(config.get("ticket_staff_reminder_minutes", 60)),
            required=True,
            max_length=5,
        )
        self.author_inactivity = TextInput(
            label="Auto-fechar inatividade do jogador (minutos)",
            placeholder="Ex: 480 (8h) ou 5 (teste)",
            default=str(config.get("ticket_author_inactivity_close_minutes", 480)),
            required=True,
            max_length=5,
        )
        self.add_item(self.close_delay)
        self.add_item(self.unanswered)
        self.add_item(self.staff_reminder)
        self.add_item(self.author_inactivity)

    async def on_submit(self, interaction: discord.Interaction):
        try:
            cd = max(1, int(self.close_delay.value.strip()))
            ua = max(1, int(self.unanswered.value.strip()))
            sr = max(1, int(self.staff_reminder.value.strip()))
            ai = max(1, int(self.author_inactivity.value.strip()))
        except ValueError:
            return await interaction.response.send_message("❌ Use apenas números inteiros nos campos.", ephemeral=True)
        config = get_guild_config(self.guild_id)
        config["ticket_close_delay_seconds"] = cd
        config["ticket_unanswered_alert_minutes"] = ua
        config["ticket_staff_reminder_minutes"] = sr
        config["ticket_author_inactivity_close_minutes"] = ai
        save_guild_config(self.guild_id, config)
        await interaction.response.send_message(
            f"✅ Tempos atualizados: fechar em **{cd}s** | alerta **{ua} min** | lembrete **{sr} min** | auto-fechar **{ai} min**.",
            ephemeral=True,
        )


class OpenTicketView(discord.ui.View):
    """View para abrir ticket. Painel PT-BR: só texto PT; painel EN: só texto EN."""

    def __init__(self, bot, config: dict, lang: str = "pt"):
        super().__init__(timeout=None)
        self.bot = bot
        self.config = config
        self.lang = lang
        label = "Abrir Ticket" if lang == "pt" else "Open Ticket"
        btn = discord.ui.Button(
            label=label,
            emoji="🎫",
            style=discord.ButtonStyle.primary,
            custom_id="sv_open_start",
        )
        btn.callback = self._open_btn_callback
        self.add_item(btn)

    async def _open_btn_callback(self, interaction: discord.Interaction):
        cog = interaction.client.get_cog("TicketCog")
        if cog:
            await cog._handle_open_ticket_start(interaction, "sv_open_start", lang=self.lang)


class ConfigEmbedView(discord.ui.View):
    """Painel moderno de configuração com Select Menus e botões."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.category],
        custom_id="sv_config_category_pt",
        placeholder="📁 Categoria PT-BR (painel brasileiro)",
        row=0,
    )
    async def select_category_pt(self, interaction: discord.Interaction, select: ChannelSelect):
        channel = select.values[0] if select.values else None
        if not channel:
            return
        config = get_guild_config(self.guild_id)
        config["category_id_pt"] = str(channel.id)
        if not config.get("category_id"):
            config["category_id"] = str(channel.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigEmbedView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Categoria PT-BR: **{channel.name}**", ephemeral=True)

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.category],
        custom_id="sv_config_category_en",
        placeholder="📁 Categoria US/EN (painel em inglês)",
        row=1,
    )
    async def select_category_en(self, interaction: discord.Interaction, select: ChannelSelect):
        channel = select.values[0] if select.values else None
        if not channel:
            return
        config = get_guild_config(self.guild_id)
        config["category_id_en"] = str(channel.id)
        if not config.get("category_id"):
            config["category_id"] = str(channel.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigEmbedView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Categoria US/EN: **{channel.name}**", ephemeral=True)

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="sv_config_logs",
        placeholder="📋 Selecione o Canal de Logs",
        row=2,
    )
    async def select_logs(self, interaction: discord.Interaction, select: ChannelSelect):
        channel = select.values[0] if select.values else None
        if not channel:
            return
        config = get_guild_config(self.guild_id)
        config["logs_channel_id"] = str(channel.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigEmbedView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Logs: {channel.mention}", ephemeral=True)

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="sv_config_transcript_ch",
        placeholder="📄 Canal de cópias de transcript (HTML)",
        row=3,
    )
    async def select_transcript_channel(self, interaction: discord.Interaction, select: ChannelSelect):
        channel = select.values[0] if select.values else None
        if not channel:
            return
        config = get_guild_config(self.guild_id)
        config["transcript_channel_id"] = str(channel.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=ConfigEmbedView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Canal de transcripts: {channel.mention}", ephemeral=True)

    @discord.ui.button(label="Adicionar Servidor", style=discord.ButtonStyle.secondary, emoji="🖥️", custom_id="sv_config_add_server", row=4)
    async def add_server(self, interaction: discord.Interaction, button: discord.ui.Button):
        """Menu efêmero: adicionar, editar ou remover servidor configurado."""
        config = get_guild_config(self.guild_id)
        servers = config.get("servers", [])

        view = discord.ui.View(timeout=120)

        async def _add(i: discord.Interaction):
            modal = AddServerModal(self.guild_id)
            await i.response.send_modal(modal)

        async def _edit(i: discord.Interaction):
            if not servers:
                return await i.response.send_message("❌ Nenhum servidor configurado para editar.", ephemeral=True)
            sel_view = discord.ui.View(timeout=60)
            options = [
                discord.SelectOption(label=s.get("name", f"ID {s.get('id')}"), value=str(s.get("id")))
                for s in servers[:25]
            ]
            select = discord.ui.Select(placeholder="Selecione o servidor para editar", options=options)

            async def _on_select_edit(sel_inter: discord.Interaction):
                sid = select.values[0]
                modal = EditServerModal(self.guild_id, sid)
                await sel_inter.response.send_modal(modal)

            select.callback = _on_select_edit
            sel_view.add_item(select)
            await i.response.send_message("Selecione o servidor que deseja **editar**:", view=sel_view, ephemeral=True)

        async def _remove(i: discord.Interaction):
            if not servers:
                return await i.response.send_message("❌ Nenhum servidor configurado para remover.", ephemeral=True)
            sel_view = discord.ui.View(timeout=60)
            options = [
                discord.SelectOption(label=s.get("name", f"ID {s.get('id')}"), value=str(s.get("id")))
                for s in servers[:25]
            ]
            select = discord.ui.Select(placeholder="Selecione o servidor para remover", options=options)

            async def _on_select_remove(sel_inter: discord.Interaction):
                sid = select.values[0]
                cfg = get_guild_config(self.guild_id)
                current = cfg.get("servers", [])
                new_list = [s for s in current if str(s.get("id")) != str(sid)]
                cfg["servers"] = new_list
                save_guild_config(self.guild_id, cfg)
                await sel_inter.response.send_message("✅ Servidor removido da configuração de tickets.", ephemeral=True)

            select.callback = _on_select_remove
            sel_view.add_item(select)
            await i.response.send_message("Selecione o servidor que deseja **remover**:", view=sel_view, ephemeral=True)

        btn_add = discord.ui.Button(label="Adicionar", style=discord.ButtonStyle.primary, emoji="➕")
        btn_edit = discord.ui.Button(label="Editar", style=discord.ButtonStyle.secondary, emoji="✏️")
        btn_remove = discord.ui.Button(label="Remover", style=discord.ButtonStyle.danger, emoji="🗑️")

        async def _btn_add_cb(i: discord.Interaction): await _add(i)
        async def _btn_edit_cb(i: discord.Interaction): await _edit(i)
        async def _btn_remove_cb(i: discord.Interaction): await _remove(i)

        btn_add.callback = _btn_add_cb
        btn_edit.callback = _btn_edit_cb
        btn_remove.callback = _btn_remove_cb

        view.add_item(btn_add)
        view.add_item(btn_edit)
        view.add_item(btn_remove)

        await interaction.response.send_message("Escolha uma ação para **servidores de tickets**:", view=view, ephemeral=True)

    @discord.ui.button(label="Editar Painel", style=discord.ButtonStyle.secondary, emoji="📺", custom_id="sv_config_edit_panel", row=4)
    async def edit_panel_embed(self, interaction: discord.Interaction, button: discord.ui.Button):
        # View efêmera só para quem clicou, com botões separados para texto/cor e banner
        view = discord.ui.View(timeout=120)

        async def _edit_text_color(i: discord.Interaction):
            config = get_guild_config(self.guild_id)
            modal = PanelEmbedModal(self.guild_id, "panel", config)
            await i.response.send_modal(modal)

        async def _edit_banner(i: discord.Interaction):
            config = get_guild_config(self.guild_id)
            modal = PanelBannerModal(self.guild_id, "panel", config)
            await i.response.send_modal(modal)

        btn_text = discord.ui.Button(label="Editar texto/cor", style=discord.ButtonStyle.primary, emoji="✏️")
        btn_banner = discord.ui.Button(label="Editar banner", style=discord.ButtonStyle.secondary, emoji="🖼️")
        async def _btn_text_cb(i: discord.Interaction): await _edit_text_color(i)
        async def _btn_banner_cb(i: discord.Interaction): await _edit_banner(i)
        btn_text.callback = _btn_text_cb
        btn_banner.callback = _btn_banner_cb
        view.add_item(btn_text)
        view.add_item(btn_banner)

        await interaction.response.send_message(
            "Escolha o que deseja editar no **painel de tickets**:", view=view, ephemeral=True
        )

    @discord.ui.button(label="Editar Embed Ticket", style=discord.ButtonStyle.secondary, emoji="📄", custom_id="sv_config_edit_ticket", row=4)
    async def edit_ticket_embed(self, interaction: discord.Interaction, button: discord.ui.Button):
        # View efêmera só para quem clicou, com botões separados para texto/cor e banner do embed do ticket
        view = discord.ui.View(timeout=120)

        async def _edit_text_color(i: discord.Interaction):
            config = get_guild_config(self.guild_id)
            modal = PanelEmbedModal(self.guild_id, "ticket", config)
            await i.response.send_modal(modal)

        async def _edit_banner(i: discord.Interaction):
            config = get_guild_config(self.guild_id)
            modal = PanelBannerModal(self.guild_id, "ticket", config)
            await i.response.send_modal(modal)

        btn_text = discord.ui.Button(label="Editar texto/cor", style=discord.ButtonStyle.primary, emoji="✏️")
        btn_banner = discord.ui.Button(label="Editar banner", style=discord.ButtonStyle.secondary, emoji="🖼️")
        async def _btn_text_cb(i: discord.Interaction): await _edit_text_color(i)
        async def _btn_banner_cb(i: discord.Interaction): await _edit_banner(i)
        btn_text.callback = _btn_text_cb
        btn_banner.callback = _btn_banner_cb
        view.add_item(btn_text)
        view.add_item(btn_banner)

        await interaction.response.send_message(
            "Escolha o que deseja editar no **embed do ticket**:", view=view, ephemeral=True
        )

    @discord.ui.button(label="Publicar Painel", style=discord.ButtonStyle.success, emoji="🚀", custom_id="sv_config_panel", row=4)
    async def publish_panel(self, interaction: discord.Interaction, button: discord.ui.Button):
        view = PublishPanelLangView(self.bot, self.guild_id)
        await interaction.response.send_message("Selecione o idioma do painel:", view=view, ephemeral=True)

    @discord.ui.button(label="Voltar", style=discord.ButtonStyle.secondary, emoji="⬅️", custom_id="sv_config_back", row=4)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        view = SupMainView(self.bot, self.guild_id)
        await interaction.response.edit_message(embed=_main_embed(), view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão para configurar.", ephemeral=True)
            return False
        return True


async def setup(bot):
    await bot.add_cog(TicketCog(bot))
