"""
Cog de Wipe — configuração de datas/horários de wipe, countdown, RCON e timezone.
!sup → RustServer → Wipe
RCON: protocolo WebRcon do Rust via websockets (ws://host:port/senha + JSON).
"""
import asyncio
import json
import re
import socket
import time
from datetime import datetime, timezone, timedelta

import discord
from discord.ext import commands
from discord.enums import ChannelType

from config import BOT_OWNER_ID
from utils.storage import get_guild_config, save_guild_config
from utils.wipe_storage import (
    get_wipe_config,
    save_wipe_config,
    get_all_wipe_guild_ids,
    DEFAULT_EMBED_OPTIONS,
    list_countdowns,
    get_countdown,
    set_countdown_message_id,
    set_countdown_embed_options,
    add_countdown,
    remove_countdown,
    update_countdown,
)
from utils.bot_logs import get_log_channel_id
from utils.translator import t


def can_use_sup(user_id: str, guild_id: str) -> bool:
    if str(user_id) == BOT_OWNER_ID:
        return True
    config = get_guild_config(guild_id)
    return str(user_id) in config.get("allowed_sup_users", [])


def color_from_hex(hex_color: str) -> int:
    hex_color = (hex_color or "#5865F2").lstrip("#")
    try:
        return int(hex_color, 16)
    except ValueError:
        return 0x5865F2


# Fuso horário padrão por região (para conversão)
_TZ_OFFSETS = {
    "BR": -3,
    "EU": 1,
    "US": -5,
}


def _br_to_utc(dt: datetime) -> datetime:
    """Assume dt em horário de Brasília (UTC-3)."""
    if dt.tzinfo is None:
        return dt.replace(tzinfo=timezone(timedelta(hours=-3))).astimezone(timezone.utc)
    return dt.astimezone(timezone.utc)


def _utc_to_local(utc_dt: datetime, offset_hours: int) -> datetime:
    return utc_dt + timedelta(hours=offset_hours)


def _format_countdown(target: datetime, lang: str = "pt") -> str:
    now = datetime.now(timezone.utc)
    if target <= now:
        return "⏰ **WIPE IN PROGRESS**" if lang == "en" else "⏰ **WIPE EM ANDAMENTO**"
    delta = target - now
    days = delta.days
    hours, rem = divmod(delta.seconds, 3600)
    mins, secs = divmod(rem, 60)
    parts = []
    if days > 0:
        parts.append(f"**{days}d**")
    parts.append(f"**{hours:02d}h**")
    parts.append(f"**{mins:02d}m**")
    parts.append(f"**{secs:02d}s**")
    return "  ".join(parts)


class WipeConfigView(discord.ui.View):
    """View de configuração do wipe."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

        # Ajusta o texto do botão de RCON conforme já exista servidor configurado ou não.
        # Assim o usuário não vê sempre "Adicionar servidor" quando já configurou o RCON.
        cfg = get_wipe_config(guild_id)
        has_rcon = bool(cfg.get("rcon_servers"))
        for child in self.children:
            cid = getattr(child, "custom_id", None)
            if cid == "wipe_add_rcon":
                if has_rcon:
                    # Já existe pelo menos um servidor RCON salvo: muda o rótulo para indicar que é configuração.
                    child.label = "🖥️ Configurar RCON"
                else:
                    # Primeiro uso: mantém a ideia de adicionar.
                    child.label = "🖥️ Adicionar RCON"
                break

    @discord.ui.select(
        cls=discord.ui.ChannelSelect,
        channel_types=[ChannelType.text],
        placeholder="📢 Canal do countdown",
        custom_id="wipe_countdown_ch",
        row=0,
    )
    async def select_countdown_channel(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        cfg = get_wipe_config(self.guild_id)
        cfg["countdown_channel_id"] = str(ch.id)
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=WipeConfigView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Canal do countdown: {ch.mention}", ephemeral=True)

    @discord.ui.button(label="📅 Definir data/hora (BR)", style=discord.ButtonStyle.primary, row=1, custom_id="wipe_set_datetime")
    async def set_datetime(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeDatetimeModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🖼️ Banner", style=discord.ButtonStyle.secondary, row=1, custom_id="wipe_set_banner")
    async def set_banner(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeBannerModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🖥️ Adicionar RCON", style=discord.ButtonStyle.secondary, row=1, custom_id="wipe_add_rcon")
    async def add_rcon(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeRconModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🔍 Buscar info RCON", style=discord.ButtonStyle.secondary, row=2, custom_id="wipe_fetch_rcon")
    async def fetch_rcon(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._fetch_rcon_info(self.guild_id, interaction)

    @discord.ui.button(label="▶️ Iniciar countdown", style=discord.ButtonStyle.success, row=3, custom_id="wipe_start_countdown")
    async def start_countdown(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._start_countdown(self.guild_id, interaction)

    @discord.ui.button(label="⏹️ Parar countdown", style=discord.ButtonStyle.danger, row=3, custom_id="wipe_stop_countdown")
    async def stop_countdown(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._stop_countdown(self.guild_id, interaction)

    @discord.ui.button(label="📋 Countdowns por sala", style=discord.ButtonStyle.secondary, row=3, custom_id="wipe_salas")
    async def countdowns_por_sala(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = _build_countdowns_sala_embed(self.guild_id)
        await interaction.response.edit_message(
            embed=embed,
            view=CountdownsPorSalaView(self.bot, self.guild_id, self.build_embed),
        )

    @discord.ui.button(label="📢 Anúncio no chat (!wipe)", style=discord.ButtonStyle.secondary, row=3, custom_id="wipe_announce_menu")
    async def announce_menu(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = _build_wipe_announce_config_embed(self.guild_id)
        await interaction.response.edit_message(embed=embed, view=WipeAnnounceConfigView(self.bot, self.guild_id, self.build_embed))

    @discord.ui.button(label="🔗 Definir Connect (servidores)", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_connect_menu")
    async def connect_menu(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = discord.Embed(
            title="🔗 URL Connect por servidor",
            description="O connect é preenchido automaticamente (host:28015). Escolha um servidor para **alterar** a URL se precisar.",
            color=color_from_hex(get_guild_config(self.guild_id).get("color", "#5865F2")),
            timestamp=datetime.utcnow(),
        )
        embed.set_footer(text="Suporte Valley • Wipe")
        await interaction.response.edit_message(embed=embed, view=ConnectSelectView(self.bot, self.guild_id, self.build_embed))

    @discord.ui.button(label="⬅️ Voltar", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_back")
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        from cogs.tickets import RustCategoryView, _rust_menu_embed
        await interaction.response.edit_message(embed=_rust_menu_embed(), view=RustCategoryView(self.bot, self.guild_id))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


def _build_countdowns_sala_embed(guild_id: str) -> discord.Embed:
    """Embed da tela Countdowns por sala."""
    cfg = get_wipe_config(guild_id)
    gcfg = get_guild_config(guild_id)
    color = color_from_hex(gcfg.get("color", "#5865F2"))
    embed = discord.Embed(
        title="📋 Countdowns por sala",
        description="Um countdown por canal (ex.: BR 10X numa sala, EU noutra). Sem limite.",
        color=color,
        timestamp=datetime.utcnow(),
    )
    countdowns = list_countdowns(guild_id)
    # Evita mostrar countdowns duplicados caso existam entradas repetidas no storage.
    seen_ids: set[str] = set()
    rcon_servers = cfg.get("rcon_servers") or []
    for cd in countdowns:
        cd_id = str(cd.get("id") or "")
        if cd_id in seen_ids:
            continue
        seen_ids.add(cd_id)
        ch_id = cd.get("channel_id")
        rcon_idx = cd.get("rcon_index", 0)
        srv_name = rcon_servers[rcon_idx].get("name", "?") if 0 <= rcon_idx < len(rcon_servers) else "?"
        status = "🟢 Ativo" if cd.get("message_id") else "⚪ Parado"
        lang = (cd.get("lang") or "pt").lower()
        lang_label = "EN-US" if lang == "en" else "PT-BR"
        interval = int(cd.get("update_interval_seconds") or 60)
        embed.add_field(
            name=f"{cd.get('label', 'Sala')} — {status}",
            value=f"Canal: <#{ch_id}>\nServidor: **{srv_name}**\nIdioma: **{lang_label}**\nAtualização: **{max(5, interval)}s**\nWipe: `{cd.get('wipe_datetime_utc', '?')[:16] or '?'}`",
            inline=False,
        )
    if not countdowns:
        embed.add_field(name="—", value="Nenhum painel configurado. Clique em **Adicionar countdown** para criar o painel base.", inline=False)
    embed.set_footer(text="Suporte Valley • Wipe por sala")
    return embed


class CountdownsPorSalaView(discord.ui.View):
    """View: listar countdowns por sala, adicionar, iniciar/parar/config/remover."""

    def __init__(self, bot, guild_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_wipe_embed = build_wipe_embed
        self._selected_cd_id: str | None = None
        countdowns = list_countdowns(guild_id)
        opts = [
            discord.SelectOption(
                label=(c.get("label") or f"Painel {i+1}")[:100],
                value=c.get("id", ""),
                description=f"Canal {c.get('channel_id')} • {'Ativo' if c.get('message_id') else 'Parado'}",
            )
            for i, c in enumerate(countdowns)
        ] if countdowns else [discord.SelectOption(label="Nenhum countdown", value="__none__")]
        for c in self.children:
            if getattr(c, "custom_id", None) == "cd_sala_select":
                c.options = opts
                break

    @discord.ui.select(placeholder="Escolha um painel para ações", row=0, custom_id="cd_sala_select")
    async def _countdown_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        self._selected_cd_id = select.values[0] if select.values and select.values[0] != "__none__" else None
        # Compatibilidade com diferentes versões/forks de discord.py:
        # algumas têm response.defer_update(), outras só response.defer().
        try:
            if hasattr(interaction.response, "defer_update"):
                await interaction.response.defer_update()
            else:
                await interaction.response.defer()
        except Exception:
            try:
                await interaction.response.defer()
            except Exception:
                pass

    @discord.ui.button(label="➕ Adicionar countdown", style=discord.ButtonStyle.primary, row=1, custom_id="cd_sala_add")
    async def add_countdown_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cfg = get_wipe_config(self.guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        if not rcon_servers:
            return await interaction.response.send_message("❌ Adicione ao menos um servidor RCON em Wipe primeiro.", ephemeral=True)
        await interaction.response.send_modal(CreatePanelModal(self.guild_id))

    @discord.ui.button(label="▶️ Iniciar", style=discord.ButtonStyle.success, row=1, custom_id="cd_sala_start")
    async def start_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._selected_cd_id:
            return await interaction.response.send_message("❌ Selecione um painel na lista.", ephemeral=True)
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._start_countdown_for_sala(self.guild_id, self._selected_cd_id, interaction)

    @discord.ui.button(label="⏹️ Parar", style=discord.ButtonStyle.danger, row=1, custom_id="cd_sala_stop")
    async def stop_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._selected_cd_id:
            return await interaction.response.send_message("❌ Selecione um painel na lista.", ephemeral=True)
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._stop_countdown_for_sala(self.guild_id, self._selected_cd_id, interaction)

    @discord.ui.button(label="🧩 Configurar painel", style=discord.ButtonStyle.secondary, row=2, custom_id="cd_sala_panel")
    async def config_panel_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._selected_cd_id:
            return await interaction.response.send_message("❌ Selecione um painel na lista.", ephemeral=True)
        cd = get_countdown(self.guild_id, self._selected_cd_id)
        if not cd:
            return await interaction.response.send_message("❌ Countdown não encontrado.", ephemeral=True)
        await interaction.response.edit_message(
            embed=_build_countdown_panel_embed(self.guild_id, cd),
            view=CountdownPanelConfigView(self.bot, self.guild_id, self._selected_cd_id, self.build_wipe_embed),
        )

    @discord.ui.button(label="🗑️ Remover", style=discord.ButtonStyle.danger, row=2, custom_id="cd_sala_remove")
    async def remove_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._selected_cd_id:
            return await interaction.response.send_message("❌ Selecione um painel na lista.", ephemeral=True)
        remove_countdown(self.guild_id, self._selected_cd_id)
        self._selected_cd_id = None
        await interaction.response.edit_message(
            embed=_build_countdowns_sala_embed(self.guild_id),
            view=CountdownsPorSalaView(self.bot, self.guild_id, self.build_wipe_embed),
        )
        await interaction.followup.send("✅ Countdown removido.", ephemeral=True)

    @discord.ui.button(label="⬅️ Voltar ao Wipe", style=discord.ButtonStyle.secondary, row=3, custom_id="cd_sala_back")
    async def back_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        embed = cog._build_wipe_embed(self.guild_id) if cog else self.build_wipe_embed(self.guild_id)
        await interaction.response.edit_message(
            embed=embed,
            view=WipeConfigView(self.bot, self.guild_id, self.build_wipe_embed),
        )

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


def _embed_options_embed(guild_id: str, cd: dict) -> discord.Embed:
    """Embed da tela de opções do embed (o que mostrar em cada painel)."""
    gcfg = get_guild_config(guild_id)
    color = color_from_hex(gcfg.get("color", "#5865F2"))
    opts = cd.get("embed_options") or DEFAULT_EMBED_OPTIONS
    embed = discord.Embed(
        title=f"⚙️ Conteúdo do embed — {cd.get('label', 'Sala')}",
        description="Marque o que deve aparecer neste countdown (nome do servidor, jogadores, lista, etc.).",
        color=color,
        timestamp=datetime.utcnow(),
    )
    for k, v in opts.items():
        name = {"show_server_name": "Nome do servidor", "show_player_count": "Contagem de jogadores", "show_player_list": "Lista de jogadores", "show_wipe_countdown": "Countdown do wipe", "show_banner": "Banner"}.get(k, k)
        embed.add_field(name=name, value="✅ Sim" if v else "❌ Não", inline=True)
    embed.set_footer(text="Use os botões abaixo para alternar")
    return embed


def _build_countdown_panel_embed(guild_id: str, cd: dict) -> discord.Embed:
    """Embed da tela de configuração de um painel (countdown por sala)."""
    gcfg = get_guild_config(guild_id)
    cfg = get_wipe_config(guild_id)
    color = color_from_hex(gcfg.get("color", "#5865F2"))
    rcon_servers = cfg.get("rcon_servers") or []
    rcon_idx = int(cd.get("rcon_index", 0) or 0)
    server_name = _server_display_name(rcon_servers[rcon_idx]) if 0 <= rcon_idx < len(rcon_servers) else "?"
    lang = (cd.get("lang") or "pt").lower()
    lang_label = "EN-US" if lang == "en" else "PT-BR"
    ch_id = cd.get("channel_id")
    cat_id = cd.get("category_id")
    interval = int(cd.get("update_interval_seconds") or 60)
    wipe_utc = (cd.get("wipe_datetime_utc") or "")[:16] or "?"
    embed = discord.Embed(
        title=f"🧩 Configurar painel — {cd.get('label', 'Sala')}",
        description="Ajuste servidor RCON, idioma e sala deste painel.",
        color=color,
        timestamp=datetime.utcnow(),
    )
    embed.add_field(name="🖥️ Servidor", value=f"**{server_name}**", inline=True)
    embed.add_field(name="🌐 Idioma", value=f"**{lang_label}**", inline=True)
    embed.add_field(name="📢 Sala atual", value=f"<#{ch_id}>" if ch_id else "`Não definida`", inline=True)
    embed.add_field(name="⏱️ Atualização", value=f"**{max(5, interval)}s**", inline=True)
    embed.add_field(name="📅 Wipe (UTC)", value=f"`{wipe_utc}`", inline=True)
    embed.add_field(name="🖼️ Banner", value="✅ Definido" if cd.get("banner_url") else "`Não definido`", inline=True)
    embed.add_field(name="📁 Categoria fallback", value=f"<#{cat_id}>" if cat_id else "`Sem categoria`", inline=False)
    embed.set_footer(text="Selecione opções abaixo e clique em Salvar")
    return embed


class CountdownPanelDetailsModal(discord.ui.Modal, title="Dados do painel"):
    """Configura nome, data/hora wipe e banner do painel."""

    def __init__(self, guild_id: str, cd_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.cd_id = cd_id
        cd = get_countdown(guild_id, cd_id) or {}
        current_label = (cd.get("label") or "Painel")[:80]
        current_banner = (cd.get("banner_url") or "")[:200]
        day_default, time_default = "", ""
        dt_utc = cd.get("wipe_datetime_utc")
        if dt_utc:
            try:
                d = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
                if d.tzinfo is None:
                    d = d.replace(tzinfo=timezone.utc)
                d_br = d.astimezone(timezone(timedelta(hours=-3)))
                day_default = d_br.strftime("%d/%m/%Y")
                time_default = d_br.strftime("%H:%M")
            except Exception:
                pass
        self.add_item(discord.ui.TextInput(label="Nome do painel", default=current_label, required=True, max_length=80))
        self.add_item(discord.ui.TextInput(label="Data wipe (DD/MM/YYYY) BR", default=day_default, required=True, max_length=10))
        self.add_item(discord.ui.TextInput(label="Hora wipe (HH:MM) BR", default=time_default, required=True, max_length=5))
        self.add_item(discord.ui.TextInput(label="URL do banner (opcional)", default=current_banner, required=False, max_length=200))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            label = (self.children[0].value or "").strip() or "Painel"
            day, month, year = map(int, self.children[1].value.strip().replace("-", "/").split("/"))
            hour, minute = map(int, self.children[2].value.strip().replace(":", " ").split())
            dt_br = datetime(year, month, day, hour, minute, 0, tzinfo=timezone(timedelta(hours=-3)))
            dt_utc = dt_br.astimezone(timezone.utc).isoformat()
            banner = (self.children[3].value or "").strip() or None
            update_countdown(self.guild_id, self.cd_id, {
                "label": label,
                "wipe_datetime_utc": dt_utc,
                "banner_url": banner,
            })
            await interaction.response.send_message("✅ Dados do painel atualizados.", ephemeral=True)
        except (ValueError, IndexError):
            await interaction.response.send_message("❌ Data/hora inválida. Use DD/MM/YYYY e HH:MM", ephemeral=True)


class CountdownUpdateIntervalModal(discord.ui.Modal, title="Tempo de atualização do painel"):
    """Configura intervalo de atualização (mínimo 5s para evitar bloqueio)."""

    def __init__(self, guild_id: str, cd_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.cd_id = cd_id
        cd = get_countdown(guild_id, cd_id) or {}
        current = str(max(5, int(cd.get("update_interval_seconds") or 60)))
        self.add_item(discord.ui.TextInput(
            label="Atualizar a cada quantos segundos?",
            placeholder="Mínimo 5",
            default=current,
            required=True,
            max_length=4,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        raw = (self.children[0].value or "").strip()
        if not raw.isdigit():
            return await interaction.response.send_message("❌ Informe um número inteiro em segundos.", ephemeral=True)
        seconds = max(5, int(raw))
        update_countdown(self.guild_id, self.cd_id, {"update_interval_seconds": seconds})
        await interaction.response.send_message(
            f"✅ Tempo de atualização salvo: **{seconds}s** (mínimo seguro: 5s).",
            ephemeral=True,
        )


class EmbedOptionsView(discord.ui.View):
    """Toggle das opções do embed (nome do servidor, jogadores, lista, countdown, banner)."""

    def __init__(self, bot, guild_id: str, cd_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.cd_id = cd_id
        self.build_wipe_embed = build_wipe_embed

    def _toggle(self, key: str):
        cd = get_countdown(self.guild_id, self.cd_id)
        if not cd:
            return
        opts = dict(cd.get("embed_options") or DEFAULT_EMBED_OPTIONS)
        opts[key] = not opts.get(key, True)
        set_countdown_embed_options(self.guild_id, self.cd_id, opts)

    @discord.ui.button(label="Nome servidor", style=discord.ButtonStyle.secondary, row=0, custom_id="eo_name")
    async def btn_name(self, interaction: discord.Interaction, button: discord.ui.Button):
        self._toggle("show_server_name")
        cd = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(embed=_embed_options_embed(self.guild_id, cd), view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed))

    @discord.ui.button(label="Qtd jogadores", style=discord.ButtonStyle.secondary, row=0, custom_id="eo_count")
    async def btn_count(self, interaction: discord.Interaction, button: discord.ui.Button):
        self._toggle("show_player_count")
        cd = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(embed=_embed_options_embed(self.guild_id, cd), view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed))

    @discord.ui.button(label="Lista jogadores", style=discord.ButtonStyle.secondary, row=0, custom_id="eo_list")
    async def btn_list(self, interaction: discord.Interaction, button: discord.ui.Button):
        self._toggle("show_player_list")
        cd = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(embed=_embed_options_embed(self.guild_id, cd), view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed))

    @discord.ui.button(label="Countdown wipe", style=discord.ButtonStyle.secondary, row=1, custom_id="eo_cd")
    async def btn_cd(self, interaction: discord.Interaction, button: discord.ui.Button):
        self._toggle("show_wipe_countdown")
        cd = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(embed=_embed_options_embed(self.guild_id, cd), view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed))

    @discord.ui.button(label="Banner", style=discord.ButtonStyle.secondary, row=1, custom_id="eo_banner")
    async def btn_banner(self, interaction: discord.Interaction, button: discord.ui.Button):
        self._toggle("show_banner")
        cd = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(embed=_embed_options_embed(self.guild_id, cd), view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed))

    @discord.ui.button(label="⬅️ Voltar", style=discord.ButtonStyle.secondary, row=2, custom_id="eo_back")
    async def back_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.edit_message(
            embed=_build_countdowns_sala_embed(self.guild_id),
            view=CountdownsPorSalaView(self.bot, self.guild_id, self.build_wipe_embed),
        )

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class CountdownPanelConfigView(discord.ui.View):
    """Configuração de painel: sala, categoria fallback, idioma e servidor RCON."""

    def __init__(self, bot, guild_id: str, cd_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.cd_id = cd_id
        self.build_wipe_embed = build_wipe_embed
        self._channel_id: int | None = None
        self._category_id: int | None = None
        self._lang: str | None = None
        self._rcon_index: int | None = None
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers") or []
        opts = [
            discord.SelectOption(
                label=_server_display_name(s)[:100],
                value=str(i),
                description=f"{s.get('host', '?')}:{s.get('port', '?')}",
            )
            for i, s in enumerate(servers)
        ] if servers else [discord.SelectOption(label="Nenhum servidor", value="-1")]
        for c in self.children:
            if getattr(c, "custom_id", None) == "cd_panel_rcon":
                c.options = opts
                break

    async def _safe_defer(self, interaction: discord.Interaction) -> None:
        try:
            if hasattr(interaction.response, "defer_update"):
                await interaction.response.defer_update()
            else:
                await interaction.response.defer()
        except Exception:
            pass

    @discord.ui.select(
        cls=discord.ui.ChannelSelect,
        channel_types=[ChannelType.text],
        placeholder="📢 Selecionar sala (canal de countdown)",
        row=0,
        custom_id="cd_panel_channel",
    )
    async def channel_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        self._channel_id = int(ch.id) if ch else None
        await self._safe_defer(interaction)

    @discord.ui.select(
        cls=discord.ui.ChannelSelect,
        channel_types=[ChannelType.category],
        placeholder="📁 Categoria fallback (se sala sumir)",
        row=1,
        custom_id="cd_panel_category",
    )
    async def category_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        self._category_id = int(ch.id) if ch else None
        await self._safe_defer(interaction)

    @discord.ui.select(
        placeholder="🌐 Idioma do painel",
        row=2,
        custom_id="cd_panel_lang",
        options=[
            discord.SelectOption(label="PT-BR", value="pt"),
            discord.SelectOption(label="EN-US", value="en"),
        ],
    )
    async def lang_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] in ("pt", "en"):
            self._lang = select.values[0]
        await self._safe_defer(interaction)

    @discord.ui.select(placeholder="🖥️ Servidor RCON", row=3, custom_id="cd_panel_rcon")
    async def rcon_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] != "-1":
            self._rcon_index = int(select.values[0])
        await self._safe_defer(interaction)

    @discord.ui.button(label="📝 Dados", style=discord.ButtonStyle.secondary, row=4, custom_id="cd_panel_details")
    async def details_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(CountdownPanelDetailsModal(self.guild_id, self.cd_id))

    @discord.ui.button(label="⚙️ Embed", style=discord.ButtonStyle.secondary, row=4, custom_id="cd_panel_embed")
    async def embed_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cd = get_countdown(self.guild_id, self.cd_id)
        if not cd:
            return await interaction.response.send_message("❌ Painel não encontrado.", ephemeral=True)
        await interaction.response.edit_message(
            embed=_embed_options_embed(self.guild_id, cd),
            view=EmbedOptionsView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed),
        )

    @discord.ui.button(label="🕒 Atualização", style=discord.ButtonStyle.secondary, row=4, custom_id="cd_panel_interval")
    async def interval_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(CountdownUpdateIntervalModal(self.guild_id, self.cd_id))

    @discord.ui.button(label="💾 Salvar", style=discord.ButtonStyle.success, row=4, custom_id="cd_panel_save")
    async def save_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cd = get_countdown(self.guild_id, self.cd_id)
        if not cd:
            return await interaction.response.send_message("❌ Countdown não encontrado.", ephemeral=True)
        updates = {}
        if self._channel_id is not None:
            updates["channel_id"] = self._channel_id
        if self._category_id is not None:
            updates["category_id"] = self._category_id
        if self._lang is not None:
            updates["lang"] = self._lang
        if self._rcon_index is not None:
            cfg = get_wipe_config(self.guild_id)
            servers = cfg.get("rcon_servers") or []
            if self._rcon_index < 0 or self._rcon_index >= len(servers):
                return await interaction.response.send_message("❌ Servidor RCON inválido.", ephemeral=True)
            updates["rcon_index"] = self._rcon_index
        if not updates:
            return await interaction.response.send_message("ℹ️ Nenhuma alteração selecionada.", ephemeral=True)
        update_countdown(self.guild_id, self.cd_id, updates)
        cd_new = get_countdown(self.guild_id, self.cd_id)
        await interaction.response.edit_message(
            embed=_build_countdown_panel_embed(self.guild_id, cd_new or cd),
            view=CountdownPanelConfigView(self.bot, self.guild_id, self.cd_id, self.build_wipe_embed),
        )
        await interaction.followup.send("✅ Painel atualizado.", ephemeral=True)

    @discord.ui.button(label="⬅️ Voltar", style=discord.ButtonStyle.secondary, row=4, custom_id="cd_panel_back")
    async def back_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.edit_message(
            embed=_build_countdowns_sala_embed(self.guild_id),
            view=CountdownsPorSalaView(self.bot, self.guild_id, self.build_wipe_embed),
        )

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


def _sanitize_channel_name(name: str) -> str:
    """Nome de canal Discord: minúsculas, hífen, sem caracteres especiais, 2-100 chars."""
    s = re.sub(r"[^a-z0-9\s-]", "", (name or "").lower().replace(" ", "-"))
    s = re.sub(r"-+", "-", s).strip("-")[:100]
    return s or "wipe"


class CreatePanelModal(discord.ui.Modal, title="Criar painel de countdown"):
    """Cria um painel base. Configuração detalhada é feita em «Configurar painel»."""

    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.add_item(discord.ui.TextInput(
            label="Nome do painel",
            placeholder="Ex: Painel 1",
            required=True,
            max_length=80,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        label = (self.children[0].value or "").strip() or "Painel"
        cfg = get_wipe_config(self.guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        if not rcon_servers:
            return await interaction.response.send_message("❌ Adicione ao menos um servidor RCON no Wipe primeiro.", ephemeral=True)
        channel_id = cfg.get("countdown_channel_id")
        if not channel_id:
            channel_id = interaction.channel.id if interaction.channel else None
        if not channel_id:
            return await interaction.response.send_message("❌ Não foi possível definir sala inicial do painel.", ephemeral=True)
        wipe_dt = cfg.get("wipe_datetime_utc")
        if not wipe_dt:
            wipe_dt = (datetime.now(timezone.utc) + timedelta(days=1)).isoformat()
        add_countdown(
            self.guild_id,
            int(channel_id),
            0,
            wipe_dt,
            banner_url=cfg.get("banner_url"),
            label=label,
            lang="pt",
            category_id=None,
        )
        await interaction.response.send_message(
            f"✅ Painel **{label}** criado. Agora selecione ele e clique em **🧩 Configurar painel**.",
            ephemeral=True,
        )


class AddCountdownSalaView(discord.ui.View):
    """View para adicionar countdown por sala: categoria (criar canal) ou canal existente + servidor RCON, depois modal."""

    def __init__(self, bot, guild_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_wipe_embed = build_wipe_embed
        self.channel_id: int | None = None
        self.category_id: int | None = None
        self.rcon_index: int = 0
        self.countdown_lang: str = "pt"
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers") or []
        rcon_opts = [
            discord.SelectOption(label=_server_display_name(s)[:100], value=str(i), description=f"{s.get('host', '?')}:{s.get('port', '?')}")
            for i, s in enumerate(servers)
        ] if servers else [discord.SelectOption(label="Nenhum servidor", value="-1")]
        for c in self.children:
            if getattr(c, "custom_id", None) == "add_cd_rcon":
                c.options = rcon_opts
                break

    async def _defer_update(self, interaction: discord.Interaction) -> None:
        """Responde à interação (defer update ou fallback para versões/forks do discord.py)."""
        try:
            if hasattr(interaction.response, "defer_update"):
                await interaction.response.defer_update()
            elif hasattr(interaction, "defer_update"):
                await interaction.defer_update()
            else:
                await interaction.response.send_message("✅", ephemeral=True)
        except (AttributeError, TypeError):
            await interaction.response.send_message("✅", ephemeral=True)

    @discord.ui.select(cls=discord.ui.ChannelSelect, channel_types=[ChannelType.category], placeholder="📁 Criar canal nesta categoria", row=0, custom_id="add_cd_cat")
    async def category_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        self.category_id = ch.id if ch else None
        await self._defer_update(interaction)

    @discord.ui.select(cls=discord.ui.ChannelSelect, channel_types=[ChannelType.text], placeholder="📢 Ou use um canal existente", row=1, custom_id="add_cd_ch")
    async def channel_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        if ch:
            self.channel_id = ch.id
        await self._defer_update(interaction)

    @discord.ui.select(
        placeholder="🌐 Idioma do countdown",
        row=2,
        custom_id="add_cd_lang",
        options=[
            discord.SelectOption(label="PT-BR", value="pt", description="Embed totalmente em português"),
            discord.SelectOption(label="EN-US", value="en", description="Embed totalmente em inglês"),
        ],
    )
    async def _lang_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] in ("pt", "en"):
            self.countdown_lang = select.values[0]
        await self._defer_update(interaction)

    @discord.ui.select(placeholder="🖥️ Servidor RCON", row=3, custom_id="add_cd_rcon")
    async def _rcon_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] != "-1":
            self.rcon_index = int(select.values[0])
        await self._defer_update(interaction)

    @discord.ui.button(label="Definir data/hora e nome", style=discord.ButtonStyle.primary, row=4, custom_id="add_cd_modal")
    async def open_modal_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.category_id is None and self.channel_id is None:
            return await interaction.response.send_message("❌ Selecione uma **categoria** (para criar canal) ou um **canal** existente.", ephemeral=True)
        cfg = get_wipe_config(self.guild_id)
        if self.rcon_index < 0 or self.rcon_index >= len(cfg.get("rcon_servers") or []):
            return await interaction.response.send_message("❌ Selecione um servidor RCON.", ephemeral=True)
        modal = WipeAddCountdownModal(
            self.guild_id,
            self.channel_id,
            self.category_id,
            self.rcon_index,
            lang=self.countdown_lang,
            use_global_wipe=False,
        )
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Usar wipe global (BR)", style=discord.ButtonStyle.secondary, row=4, custom_id="add_cd_global")
    async def open_global_modal_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.category_id is None and self.channel_id is None:
            return await interaction.response.send_message("❌ Selecione uma **categoria** ou um **canal** existente.", ephemeral=True)
        cfg = get_wipe_config(self.guild_id)
        if not cfg.get("wipe_datetime_utc"):
            return await interaction.response.send_message("❌ Defina a data/hora do wipe no menu **Wipe** (📅 Definir data/hora BR) primeiro.", ephemeral=True)
        if self.rcon_index < 0 or self.rcon_index >= len(cfg.get("rcon_servers") or []):
            return await interaction.response.send_message("❌ Selecione um servidor RCON.", ephemeral=True)
        modal = WipeAddCountdownModal(
            self.guild_id,
            self.channel_id,
            self.category_id,
            self.rcon_index,
            lang=self.countdown_lang,
            use_global_wipe=True,
        )
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="⬅️ Cancelar", style=discord.ButtonStyle.secondary, row=4, custom_id="add_cd_cancel")
    async def cancel_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.edit_message(
            embed=_build_countdowns_sala_embed(self.guild_id),
            view=CountdownsPorSalaView(self.bot, self.guild_id, self.build_wipe_embed),
        )

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class WipeAddCountdownModal(discord.ui.Modal, title="Painel de countdown — data e nome"):
    def __init__(
        self,
        guild_id: str,
        channel_id: int | None,
        category_id: int | None,
        rcon_index: int,
        lang: str = "pt",
        use_global_wipe: bool = False,
    ):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.channel_id = channel_id
        self.category_id = category_id
        self.rcon_index = rcon_index
        self.lang = lang if lang in ("pt", "en") else "pt"
        self.use_global_wipe = use_global_wipe
        self.add_item(discord.ui.TextInput(label="Nome do painel (ex: Painel 1)", placeholder="Painel 1 — usado também como nome do canal", required=True, max_length=80))
        if not use_global_wipe:
            self.add_item(discord.ui.TextInput(label="Data wipe (DD/MM/YYYY)", placeholder="27/02/2026", required=True, max_length=10))
            self.add_item(discord.ui.TextInput(label="Hora wipe (HH:MM) BR", placeholder="15:00", required=True, max_length=5))
        self.add_item(discord.ui.TextInput(label="URL do banner (opcional)", placeholder="https://...", required=False, max_length=200))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            label = (self.children[0].value or "").strip() or "Sala"
            banner = (self.children[-1].value or "").strip() or None
            if self.use_global_wipe:
                cfg = get_wipe_config(self.guild_id)
                dt_utc_str = cfg.get("wipe_datetime_utc")
                if not dt_utc_str:
                    return await interaction.response.send_message("❌ Wipe global não configurado. Defina no menu Wipe.", ephemeral=True)
            else:
                day, month, year = map(int, self.children[1].value.strip().replace("-", "/").split("/"))
                hour, minute = map(int, self.children[2].value.strip().replace(":", " ").split())
                dt_br = datetime(year, month, day, hour, minute, 0, tzinfo=timezone(timedelta(hours=-3)))
                dt_utc_str = dt_br.astimezone(timezone.utc).isoformat()

            channel_id_to_use = self.channel_id
            if self.category_id and interaction.guild:
                name_for_channel = _sanitize_channel_name(label)
                category = interaction.guild.get_channel(int(self.category_id))
                if category and isinstance(category, discord.CategoryChannel):
                    new_ch = await interaction.guild.create_text_channel(name_for_channel, category=category)
                    channel_id_to_use = new_ch.id
                else:
                    await interaction.response.send_message("❌ Categoria não encontrada.", ephemeral=True)
                    return
            if not channel_id_to_use:
                await interaction.response.send_message("❌ Selecione um canal ou uma categoria.", ephemeral=True)
                return

            add_countdown(
                self.guild_id,
                channel_id_to_use,
                self.rcon_index,
                dt_utc_str,
                banner_url=banner,
                label=label,
                lang=self.lang,
                category_id=self.category_id,
            )
            msg = "✅ Canal criado na categoria e countdown **{0}** adicionado." if (self.category_id and interaction.guild) else "✅ Countdown **{0}** adicionado."
            await interaction.response.send_message((msg + " Use «Countdowns por sala» para iniciar.").format(label), ephemeral=True)
        except (ValueError, IndexError):
            await interaction.response.send_message("❌ Data/hora inválida. Use DD/MM/YYYY e HH:MM", ephemeral=True)


class WipeDatetimeModal(discord.ui.Modal, title="Data/hora do wipe (BR)"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.add_item(discord.ui.TextInput(label="Data (DD/MM/YYYY)", placeholder="Ex: 27/02/2026", required=True, max_length=10))
        self.add_item(discord.ui.TextInput(label="Hora (HH:MM)", placeholder="Ex: 15:00", required=True, max_length=5))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            day, month, year = map(int, self.children[0].value.strip().replace("-", "/").split("/"))
            hour, minute = map(int, self.children[1].value.strip().replace(":", " ").split())
            dt_br = datetime(year, month, day, hour, minute, 0, tzinfo=timezone(timedelta(hours=-3)))
            dt_utc = dt_br.astimezone(timezone.utc)
            cfg = get_wipe_config(self.guild_id)
            cfg["wipe_datetime_br"] = dt_br.isoformat()
            cfg["wipe_datetime_utc"] = dt_utc.isoformat()
            save_wipe_config(self.guild_id, cfg)
            await interaction.response.send_message(
                f"✅ Wipe configurado: **{dt_br.strftime('%d/%m/%Y %H:%M')}** (BR) = **{dt_utc.strftime('%d/%m/%Y %H:%M')}** UTC",
                ephemeral=True,
            )
        except (ValueError, IndexError):
            await interaction.response.send_message("❌ Formato inválido. Use DD/MM/YYYY e HH:MM", ephemeral=True)


class WipeRconModal(discord.ui.Modal, title="Configurar servidor RCON"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        cfg = get_wipe_config(guild_id)
        draft = cfg.get("rcon_draft") or {}
        self.add_item(discord.ui.TextInput(
            label="Host (IP ou domínio)",
            placeholder="Ex: play.rustvalley.com.br",
            default=draft.get("host", ""),
            required=True,
            max_length=100,
        ))
        self.add_item(discord.ui.TextInput(
            label="Porta WebRcon",
            placeholder="Ex: 28016",
            default=draft.get("port", ""),
            required=True,
            max_length=5,
        ))
        self.add_item(discord.ui.TextInput(
            label="Senha RCON",
            placeholder="Senha do webrcon",
            default=draft.get("password", ""),
            required=True,
            max_length=100,
        ))
        self.add_item(discord.ui.TextInput(
            label="Nome (opcional)",
            placeholder="Ex: BR 10X — ou deixe em branco para usar o nome do servidor (RCON)",
            default=draft.get("name", ""),
            required=False,
            max_length=50,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            host = self.children[0].value.strip()
            port = int(self.children[1].value.strip())
            password = self.children[2].value.strip()
            name = (self.children[3].value or "").strip() or host
            cfg = get_wipe_config(self.guild_id)
            servers = cfg.get("rcon_servers", [])
            servers.append({"host": host, "port": port, "password": password, "name": name, "game_port": 28015})
            cfg["rcon_servers"] = servers[-20:]
            cfg["rcon_draft"] = {"host": host, "port": str(port), "password": password, "name": name}
            save_wipe_config(self.guild_id, cfg)
            await interaction.response.send_message(
                f"✅ Servidor **{name}** adicionado. Verificando conexão e enviando log no canal de RCON...",
                ephemeral=True,
            )
            cog = interaction.client.get_cog("WipeCog")
            if cog and isinstance(cog, WipeCog):
                asyncio.create_task(cog._check_and_log_new_rcon_server(self.guild_id, host, port, password, name))
        except ValueError:
            await interaction.response.send_message("❌ Porta inválida.", ephemeral=True)


class WipeBannerModal(discord.ui.Modal, title="Banner do wipe"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        cfg = get_wipe_config(guild_id)
        self.add_item(discord.ui.TextInput(
            label="URL do banner",
            placeholder="https://...",
            default=cfg.get("banner_url") or "",
            required=False,
            max_length=500,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        url = (self.children[0].value or "").strip() or None
        cfg = get_wipe_config(self.guild_id)
        cfg["banner_url"] = url
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.send_message(f"✅ Banner {'definido' if url else 'removido'}.", ephemeral=True)


def _server_display_name(s: dict) -> str:
    """Nome do servidor para exibir (detectado pelo RCON ou nome configurado, nunca IP)."""
    return (s.get("detected_name") or s.get("name") or s.get("host") or "?").strip()[:100]


def _get_connect_url(s: dict) -> str | None:
    """URL de connect: a configurada ou steam://connect/{host}:{game_port}. Porta do jogo Rust = 28015."""
    url = (s.get("connect_url") or "").strip()
    if url and (url.startswith("http") or url.startswith("steam:")):
        return url
    host = (s.get("host") or "").strip()
    if not host:
        return None
    game_port = int(s.get("game_port") or 28015)
    return f"steam://connect/{host}:{game_port}"


# Textos da embed de anúncio por idioma (pré-definidos)
_WIPE_ANNOUNCE_LABELS = {
    "pt": {"map": "🗺️ Mapa", "aviso": "📝 Aviso de atualização", "loja": "🛒 Loja", "connect": "🔗 Connect"},
    "en": {"map": "🗺️ Map", "aviso": "📝 Update notice", "loja": "🛒 Store", "connect": "🔗 Connect"},
}


def _build_wipe_announce_config_embed(guild_id: str) -> discord.Embed:
    """Embed de configuração do anúncio (!wipe) — config separada PT-BR e US."""
    cfg = get_wipe_config(guild_id)
    gcfg = get_guild_config(guild_id)
    color = color_from_hex(gcfg.get("color", "#5865F2"))
    embed = discord.Embed(
        title="📢 Anúncio de Wipe no Chat (!wipe)",
        description="Configure **PT-BR** e **US** separadamente. O nome do servidor é detectado pelo RCON. O Connect é gerado automaticamente (host:28015); use «Definir Connect» no menu Wipe para alterar.",
        color=color,
        timestamp=datetime.utcnow(),
    )
    rcon_servers = cfg.get("rcon_servers") or []
    apt = cfg.get("wipe_announce_pt") or {}
    aus = cfg.get("wipe_announce_us") or {}
    embed.add_field(name="🇧🇷 PT-BR", value=f"Canal: <#{apt.get('channel_id')}>" if apt.get("channel_id") else "Canal: `Não definido`", inline=False)
    srv_pt = rcon_servers[apt.get("rcon_index", 0)] if 0 <= apt.get("rcon_index", 0) < len(rcon_servers) else None
    embed.add_field(name="🖥️ Nome na embed PT", value=_server_display_name(srv_pt) if srv_pt else "`Nenhum`", inline=True)
    embed.add_field(name="🗺️ Mapa PT", value=(apt.get("map_link") or "`—`")[:40] + "…" if apt.get("map_link") and len(apt.get("map_link") or "") > 40 else (apt.get("map_link") or "`—`"), inline=True)
    embed.add_field(name="🇺🇸 US", value=f"Canal: <#{aus.get('channel_id')}>" if aus.get("channel_id") else "Canal: `Não definido`", inline=False)
    srv_us = rcon_servers[aus.get("rcon_index", 0)] if 0 <= aus.get("rcon_index", 0) < len(rcon_servers) else None
    embed.add_field(name="🖥️ Nome na embed US", value=_server_display_name(srv_us) if srv_us else "`Nenhum`", inline=True)
    embed.add_field(name="🗺️ Mapa US", value=(aus.get("map_link") or "`—`")[:40] + "…" if aus.get("map_link") and len(aus.get("map_link") or "") > 40 else (aus.get("map_link") or "`—`"), inline=True)
    conn_servers = [s for s in rcon_servers if _get_connect_url(s)]
    conn_txt = "\n".join(f"• **{_server_display_name(s)}**" for s in conn_servers[:5]) if conn_servers else "`Auto (host:28015)`"
    embed.add_field(name="🔗 Connect (1 botão por servidor)", value=conn_txt[:200], inline=False)
    embed.set_footer(text="Suporte Valley • Config em !sup → Rust → Wipe")
    return embed


class WipeAnnounceConfigModal(discord.ui.Modal, title="Configurar anúncio (mapa, loja, aviso)"):
    def __init__(self, guild_id: str, lang: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.lang = lang
        cfg = get_wipe_config(guild_id)
        key = "wipe_announce_pt" if lang == "pt" else "wipe_announce_us"
        data = cfg.get(key) or {}
        title = "PT-BR" if lang == "pt" else "US"
        self.add_item(discord.ui.TextInput(label=f"Link do mapa ({title})", placeholder="https://...", default=data.get("map_link") or "", required=False, max_length=500))
        self.add_item(discord.ui.TextInput(label=f"URL da Loja ({title})", placeholder="https://...", default=data.get("store_url") or "", required=False, max_length=500))
        self.add_item(discord.ui.TextInput(label=f"Aviso de atualização ({title})", placeholder="Ex: Atualização de veículos", default=data.get("aviso") or "", required=False, style=discord.TextStyle.paragraph, max_length=500))

    async def on_submit(self, interaction: discord.Interaction):
        cfg = get_wipe_config(self.guild_id)
        key = "wipe_announce_pt" if self.lang == "pt" else "wipe_announce_us"
        data = dict(cfg.get(key) or {})
        data["map_link"] = (self.children[0].value or "").strip() or None
        data["store_url"] = (self.children[1].value or "").strip() or None
        data["aviso"] = (self.children[2].value or "").strip() or None
        if data.get("rcon_index") is None:
            data["rcon_index"] = 0
        cfg[key] = data
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.send_message(f"✅ Anúncio **{'PT-BR' if self.lang == 'pt' else 'US'}** salvo.", ephemeral=True)


class ConnectSelectView(discord.ui.View):
    """Escolher servidor para definir/alterar URL Connect (override do automático)."""

    def __init__(self, bot, guild_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_wipe_embed = build_wipe_embed
        cfg = get_wipe_config(guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        opts = [discord.SelectOption(label=_server_display_name(s)[:100], value=str(i)) for i, s in enumerate(rcon_servers)]
        if not opts:
            opts = [discord.SelectOption(label="Nenhum servidor", value="-1")]
        for c in self.children:
            if getattr(c, "custom_id", None) == "connect_select":
                c.options = opts
                break

    @discord.ui.select(placeholder="Escolha o servidor (por nome)", row=0, custom_id="connect_select")
    async def connect_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if not select.values or select.values[0] == "-1":
            return await interaction.response.defer_update()
        await interaction.response.send_modal(SetConnectUrlModal(self.guild_id, int(select.values[0])))

    @discord.ui.button(label="⬅️ Voltar ao Wipe", style=discord.ButtonStyle.secondary, row=1, custom_id="connect_back")
    async def back_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        embed = cog._build_wipe_embed(self.guild_id) if cog else self.build_wipe_embed(self.guild_id)
        await interaction.response.edit_message(embed=embed, view=WipeConfigView(self.bot, self.guild_id, self.build_wipe_embed))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class SetConnectUrlModal(discord.ui.Modal, title="URL Connect do servidor"):
    """Override do connect (padrão: steam://connect/host:28015). Deixe em branco para usar o automático."""

    def __init__(self, guild_id: str, rcon_index: int):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.rcon_index = rcon_index
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers") or []
        srv = servers[rcon_index] if 0 <= rcon_index < len(servers) else {}
        current = srv.get("connect_url") or _get_connect_url(srv) or ""
        self.add_item(discord.ui.TextInput(
            label="URL Connect (vazio = automático host:28015)",
            placeholder="steam://connect/ip:porta",
            default=current,
            required=False,
            max_length=500,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        cfg = get_wipe_config(self.guild_id)
        servers = list(cfg.get("rcon_servers") or [])
        if 0 <= self.rcon_index < len(servers):
            val = (self.children[0].value or "").strip() or None
            servers[self.rcon_index] = {**servers[self.rcon_index], "connect_url": val}
            cfg["rcon_servers"] = servers
            save_wipe_config(self.guild_id, cfg)
        await interaction.response.send_message("✅ URL Connect atualizada. (Vazio = automático)", ephemeral=True)


class WipeAnnounceConfigView(discord.ui.View):
    """View no menu !sup → Rust → Wipe: configurar anúncio PT-BR e US separadamente."""

    def __init__(self, bot, guild_id: str, build_wipe_embed):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_wipe_embed = build_wipe_embed
        cfg = get_wipe_config(guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        opts = [discord.SelectOption(label=_server_display_name(s)[:100], value=str(i)) for i, s in enumerate(rcon_servers)]
        if not opts:
            opts = [discord.SelectOption(label="Nenhum servidor", value="-1")]
        for c in self.children:
            cid = getattr(c, "custom_id", None)
            if cid in ("wipe_announce_rcon_pt", "wipe_announce_rcon_us"):
                c.options = opts

    @discord.ui.select(cls=discord.ui.ChannelSelect, channel_types=[ChannelType.text], placeholder="📢 Canal PT-BR", row=0, custom_id="wipe_announce_ch_pt")
    async def channel_pt_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        cfg = get_wipe_config(self.guild_id)
        apt = dict(cfg.get("wipe_announce_pt") or {})
        apt["channel_id"] = str(ch.id) if ch else None
        cfg["wipe_announce_pt"] = apt
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(embed=_build_wipe_announce_config_embed(self.guild_id), view=WipeAnnounceConfigView(self.bot, self.guild_id, self.build_wipe_embed))
        await interaction.followup.send(f"✅ Canal PT-BR: {ch.mention}" if ch else "✅ Canal PT-BR desmarcado.", ephemeral=True)

    @discord.ui.select(cls=discord.ui.ChannelSelect, channel_types=[ChannelType.text], placeholder="📢 Canal US", row=1, custom_id="wipe_announce_ch_us")
    async def channel_us_select(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        cfg = get_wipe_config(self.guild_id)
        aus = dict(cfg.get("wipe_announce_us") or {})
        aus["channel_id"] = str(ch.id) if ch else None
        cfg["wipe_announce_us"] = aus
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(embed=_build_wipe_announce_config_embed(self.guild_id), view=WipeAnnounceConfigView(self.bot, self.guild_id, self.build_wipe_embed))
        await interaction.followup.send(f"✅ Canal US: {ch.mention}" if ch else "✅ Canal US desmarcado.", ephemeral=True)

    @discord.ui.select(placeholder="🖥️ Nome na embed PT-BR", row=2, custom_id="wipe_announce_rcon_pt")
    async def rcon_pt_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] != "-1":
            cfg = get_wipe_config(self.guild_id)
            apt = dict(cfg.get("wipe_announce_pt") or {})
            apt["rcon_index"] = int(select.values[0])
            cfg["wipe_announce_pt"] = apt
            save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(embed=_build_wipe_announce_config_embed(self.guild_id), view=WipeAnnounceConfigView(self.bot, self.guild_id, self.build_wipe_embed))
        await interaction.followup.send("✅ Servidor (nome PT) atualizado.", ephemeral=True)

    @discord.ui.select(placeholder="🖥️ Nome na embed US", row=3, custom_id="wipe_announce_rcon_us")
    async def rcon_us_select(self, interaction: discord.Interaction, select: discord.ui.Select):
        if select.values and select.values[0] != "-1":
            cfg = get_wipe_config(self.guild_id)
            aus = dict(cfg.get("wipe_announce_us") or {})
            aus["rcon_index"] = int(select.values[0])
            cfg["wipe_announce_us"] = aus
            save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(embed=_build_wipe_announce_config_embed(self.guild_id), view=WipeAnnounceConfigView(self.bot, self.guild_id, self.build_wipe_embed))
        await interaction.followup.send("✅ Servidor (nome US) atualizado.", ephemeral=True)

    @discord.ui.button(label="⚙️ Configurar PT-BR", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_announce_config_pt")
    async def config_pt_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(WipeAnnounceConfigModal(self.guild_id, "pt"))

    @discord.ui.button(label="⚙️ Configurar US", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_announce_config_us")
    async def config_us_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(WipeAnnounceConfigModal(self.guild_id, "en"))

    @discord.ui.button(label="⬅️ Voltar ao Wipe", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_announce_back")
    async def back_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        embed = cog._build_wipe_embed(self.guild_id) if cog else self.build_wipe_embed(self.guild_id)
        await interaction.response.edit_message(embed=embed, view=WipeConfigView(self.bot, self.guild_id, self.build_wipe_embed))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class WipeSendView(discord.ui.View):
    """View do comando !wipe: só escolher idioma para enviar (PT-BR ou US)."""

    def __init__(self, bot, guild_id: str):
        super().__init__(timeout=120)
        self.bot = bot
        self.guild_id = guild_id

    @discord.ui.button(label="🇧🇷 Enviar em PT-BR", style=discord.ButtonStyle.primary, row=0, custom_id="wipe_send_pt")
    async def send_pt(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self._send(interaction, "pt")

    @discord.ui.button(label="🇺🇸 Enviar em US (English)", style=discord.ButtonStyle.primary, row=0, custom_id="wipe_send_en")
    async def send_en(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self._send(interaction, "en")

    async def _send(self, interaction: discord.Interaction, lang: str):
        cfg = get_wipe_config(self.guild_id)
        key = "wipe_announce_pt" if lang == "pt" else "wipe_announce_us"
        data = cfg.get(key) or {}
        ch_id = data.get("channel_id")
        if not ch_id:
            which = "PT-BR" if lang == "pt" else "US"
            return await interaction.response.send_message(f"❌ Configure o canal **{which}** em !sup → Rust → Wipe → Anúncio no chat.", ephemeral=True)
        guild = interaction.guild
        if not guild:
            return await interaction.response.send_message("❌ Servidor não encontrado.", ephemeral=True)
        channel = guild.get_channel(int(ch_id))
        if not channel or not isinstance(channel, discord.TextChannel):
            return await interaction.response.send_message("❌ Canal não encontrado.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        cog = interaction.client.get_cog("WipeCog")
        if not cog or not isinstance(cog, WipeCog):
            return await interaction.followup.send("❌ Módulo de wipe não disponível.", ephemeral=True)
        embed, view = await cog._build_wipe_announce_message(self.guild_id, lang)
        if not embed:
            return await interaction.followup.send("❌ Configure ao menos um servidor RCON em !sup → Rust → Wipe.", ephemeral=True)
        try:
            await channel.send(embed=embed, view=view)
            which = "PT-BR" if lang == "pt" else "US"
            await interaction.followup.send(f"✅ Anúncio **{which}** enviado em {channel.mention}.", ephemeral=True)
        except discord.Forbidden:
            await interaction.followup.send("❌ Sem permissão para enviar no canal.", ephemeral=True)
        except Exception as e:
            await interaction.followup.send(f"❌ Erro: {str(e)[:100]}", ephemeral=True)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class WipeCog(commands.Cog):
    """Cog de configuração de wipe."""

    def __init__(self, bot):
        self.bot = bot
        self._countdown_tasks: dict[str, asyncio.Task] = {}

    @commands.Cog.listener()
    async def on_ready(self):
        """Ao ficar pronto, restaura as tasks de countdown para guilds que já tinham countdown ativo."""
        for guild_id in get_all_wipe_guild_ids():
            if self._has_any_active_countdown(guild_id):
                self._ensure_countdown_task(guild_id)

    @commands.command(name="wipe")
    async def wipe_command(self, ctx: commands.Context):
        """Abre menu para escolher idioma (PT-BR ou US) e enviar o anúncio no canal configurado em !sup → Rust → Wipe."""
        if not ctx.guild:
            return await ctx.send("Use este comando em um servidor.")
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Sem permissão para usar este comando.")
        embed = discord.Embed(
            title="📢 Enviar anúncio de wipe",
            description="Escolha o idioma da embed. O anúncio será enviado no canal configurado em **!sup → Rust → Wipe → Anúncio no chat**.\n\n• **PT-BR** → canal PT-BR\n• **US** → canal US (English)",
            color=color_from_hex(get_guild_config(str(ctx.guild.id)).get("color", "#5865F2")),
            timestamp=datetime.utcnow(),
        )
        embed.set_footer(text="Suporte Valley • !wipe")
        await ctx.send(embed=embed, view=WipeSendView(self.bot, str(ctx.guild.id)))

    async def _build_wipe_announce_message(self, guild_id: str, lang: str = "pt") -> tuple[discord.Embed | None, discord.ui.View | None]:
        """Monta a embed e a view (1 Loja + 1 Connect por servidor) no idioma. Nome e connect do RCON."""
        cfg = get_wipe_config(guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        key = "wipe_announce_pt" if lang == "pt" else "wipe_announce_us"
        data = cfg.get(key) or {}
        idx = data.get("rcon_index", 0)
        if idx < 0 or idx >= len(rcon_servers):
            return None, None
        labels = _WIPE_ANNOUNCE_LABELS.get(lang) or _WIPE_ANNOUNCE_LABELS["pt"]
        srv = rcon_servers[idx]
        host = srv.get("host") or ""
        port = int(srv.get("port") or 28016)
        pw = srv.get("password") or ""
        try:
            info = await self._rcon_fetch_info(host, port, pw)
            server_name = (info.get("hostname") or "").strip() or _server_display_name(srv)
        except Exception:
            server_name = _server_display_name(srv)
        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        embed = discord.Embed(
            title=server_name[:256],
            color=color,
            timestamp=datetime.utcnow(),
        )
        map_link = (data.get("map_link") or "").strip()
        if map_link:
            embed.add_field(name=labels["map"], value=map_link, inline=False)
        aviso = (data.get("aviso") or "").strip()
        if aviso:
            embed.add_field(name=labels["aviso"], value=aviso[:1024], inline=False)
        embed.set_footer(text="Suporte Valley • Wipe")
        view = discord.ui.View()
        store_url = (data.get("store_url") or "").strip()
        if store_url and (store_url.startswith("http") or store_url.startswith("https")):
            view.add_item(discord.ui.Button(label=labels["loja"], url=store_url, style=discord.ButtonStyle.link))
        for s in rcon_servers:
            url = _get_connect_url(s)
            if url:
                view.add_item(discord.ui.Button(label=f"🔗 {_server_display_name(s)[:80]}", url=url, style=discord.ButtonStyle.link))
        return embed, view

    def _build_wipe_embed(self, guild_id: str) -> discord.Embed:
        cfg = get_wipe_config(guild_id)
        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))

        embed = discord.Embed(
            title="🗓️ Configuração de Wipe",
            description="Configure data/hora do wipe (horário BR), canal do countdown e banner.",
            color=color,
            timestamp=datetime.utcnow(),
        )
        dt_br = cfg.get("wipe_datetime_br")
        dt_utc = cfg.get("wipe_datetime_utc")
        if dt_br:
            try:
                d = datetime.fromisoformat(dt_br.replace("Z", "+00:00"))
                embed.add_field(name="📅 Data/hora (BR)", value=d.strftime("%d/%m/%Y %H:%M"), inline=True)
            except Exception:
                embed.add_field(name="📅 Data/hora (BR)", value=dt_br[:16], inline=True)
        else:
            embed.add_field(name="📅 Data/hora (BR)", value="`Não definido`", inline=True)

        if dt_utc:
            try:
                d_utc = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
                if d_utc.tzinfo is None:
                    d_utc = d_utc.replace(tzinfo=timezone.utc)
                embed.add_field(name="🌍 UTC", value=d_utc.strftime("%d/%m/%Y %H:%M"), inline=True)
                br_time = _utc_to_local(d_utc, -3)
                eu_time = _utc_to_local(d_utc, 1)
                us_time = _utc_to_local(d_utc, -5)
                embed.add_field(
                    name="🕐 Fusos horários",
                    value=f"**BR:** {br_time.strftime('%d/%m %H:%M')}\n**EU:** {eu_time.strftime('%d/%m %H:%M')}\n**US:** {us_time.strftime('%d/%m %H:%M')}",
                    inline=False,
                )
            except Exception:
                pass

        ch_id = cfg.get("countdown_channel_id")
        embed.add_field(name="📢 Canal countdown", value=f"<#{ch_id}>" if ch_id else "`Não definido`", inline=True)
        embed.add_field(name="🖼️ Banner", value="✅ Definido" if cfg.get("banner_url") else "`Não definido`", inline=True)
        rcon_servers = cfg.get("rcon_servers", [])
        embed.add_field(
            name="🖥️ Servidores RCON",
            value="\n".join(f"• **{_server_display_name(s)}** — {s.get('host', '?')}:{s.get('port', '?')}" for s in rcon_servers[:5]) or "`Nenhum configurado`",
            inline=False,
        )
        embed.set_footer(text="Suporte Valley • Wipe")
        return embed

    async def _start_countdown(self, guild_id: str, interaction: discord.Interaction):
        cfg = get_wipe_config(guild_id)
        ch_id = cfg.get("countdown_channel_id")
        dt_utc = cfg.get("wipe_datetime_utc")
        if not ch_id or not dt_utc:
            return await interaction.response.send_message(
                "❌ Defina o canal do countdown e a data/hora do wipe primeiro.",
                ephemeral=True,
            )
        try:
            target = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
            if target.tzinfo is None:
                target = target.replace(tzinfo=timezone.utc)
        except Exception:
            return await interaction.response.send_message("❌ Data/hora inválida.", ephemeral=True)

        await interaction.response.defer(ephemeral=True)
        guild = self.bot.get_guild(int(guild_id))
        if not guild:
            return await interaction.followup.send("❌ Servidor não encontrado.", ephemeral=True)
        channel = guild.get_channel(int(ch_id))
        if not channel or not isinstance(channel, discord.TextChannel):
            return await interaction.followup.send("❌ Canal não encontrado.", ephemeral=True)

        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        banner = cfg.get("banner_url")

        embed = discord.Embed(
            title="⏱️ COUNTDOWN PARA O WIPE",
            description=_format_countdown(target),
            color=color,
            timestamp=datetime.utcnow(),
        )
        if banner:
            embed.set_image(url=banner)
        embed.set_footer(text="Atualiza a cada minuto")
        msg = await channel.send(embed=embed)
        cfg["countdown_message_id"] = str(msg.id)
        save_wipe_config(guild_id, cfg)

        if guild_id in self._countdown_tasks:
            self._countdown_tasks[guild_id].cancel()
        self._countdown_tasks[guild_id] = asyncio.create_task(
            self._countdown_loop(guild_id)
        )
        await interaction.followup.send("✅ Countdown iniciado.", ephemeral=True)

    def _has_any_active_countdown(self, guild_id: str) -> bool:
        """True se há countdown legado ativo ou algum countdown por sala com message_id."""
        cfg = get_wipe_config(guild_id)
        if cfg.get("countdown_message_id"):
            return True
        for cd in list_countdowns(guild_id):
            if cd.get("message_id"):
                return True
        return False

    def _ensure_countdown_task(self, guild_id: str) -> None:
        """Garante que o task de countdown está rodando para a guild (se houver algum ativo)."""
        if not self._has_any_active_countdown(guild_id):
            return
        if guild_id in self._countdown_tasks:
            return
        self._countdown_tasks[guild_id] = asyncio.create_task(self._countdown_loop(guild_id))

    async def _start_countdown_for_sala(self, guild_id: str, cd_id: str, interaction: discord.Interaction):
        """Inicia o countdown de uma sala: envia a mensagem e inicia/retoma o task."""
        cd = get_countdown(guild_id, cd_id)
        if not cd:
            return await interaction.response.send_message("❌ Countdown não encontrado.", ephemeral=True)
        if cd.get("message_id"):
            return await interaction.response.send_message("✅ Este countdown já está ativo.", ephemeral=True)
        cfg = get_wipe_config(guild_id)
        rcon_servers = cfg.get("rcon_servers") or []
        rcon_idx = cd.get("rcon_index", 0)
        if rcon_idx < 0 or rcon_idx >= len(rcon_servers):
            return await interaction.response.send_message("❌ Servidor RCON inválido para esta sala.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        guild = self.bot.get_guild(int(guild_id))
        if not guild:
            return await interaction.followup.send("❌ Servidor não encontrado.", ephemeral=True)
        srv = rcon_servers[rcon_idx]
        channel = guild.get_channel(int(cd["channel_id"]))
        if not channel or not isinstance(channel, discord.TextChannel):
            # Se a sala configurada não existir mais, recria automaticamente com o nome do servidor via RCON.
            try:
                info_name = await self._rcon_fetch_info(
                    srv.get("host") or "",
                    int(srv.get("port") or 28016),
                    srv.get("password") or "",
                )
                channel_name = _sanitize_channel_name(
                    (info_name.get("hostname") or _server_display_name(srv) or cd.get("label") or "wipe")
                )
            except Exception:
                channel_name = _sanitize_channel_name(_server_display_name(srv) or cd.get("label") or "wipe")
            category = None
            cat_id = cd.get("category_id")
            if cat_id:
                cat_obj = guild.get_channel(int(cat_id))
                if cat_obj and isinstance(cat_obj, discord.CategoryChannel):
                    category = cat_obj
            channel = await guild.create_text_channel(channel_name, category=category)
            update_countdown(guild_id, cd_id, {"channel_id": channel.id})
        full_info = await self._rcon_fetch_full_info(
            srv.get("host") or "",
            int(srv.get("port") or 28016),
            srv.get("password") or "",
        )
        target_dt = None
        if cd.get("wipe_datetime_utc"):
            try:
                target_dt = datetime.fromisoformat(cd["wipe_datetime_utc"].replace("Z", "+00:00"))
                if target_dt.tzinfo is None:
                    target_dt = target_dt.replace(tzinfo=timezone.utc)
            except Exception:
                pass
        host = srv.get("host") or ""
        tz_name = await self._get_timezone_from_ip(host)
        local_wipe_str = self._format_wipe_local(target_dt, tz_name) if target_dt else ""
        embed = self._build_embed_for_countdown(guild_id, cd, full_info, target_dt, local_wipe_str=local_wipe_str or None)
        if not embed:
            return await interaction.followup.send("❌ Não foi possível montar a embed.", ephemeral=True)
        msg = await channel.send(embed=embed)
        set_countdown_message_id(guild_id, cd_id, msg.id)
        self._ensure_countdown_task(guild_id)
        await interaction.followup.send("✅ Countdown desta sala iniciado.", ephemeral=True)

    async def _stop_countdown_for_sala(self, guild_id: str, cd_id: str, interaction: discord.Interaction):
        """Para o countdown de uma sala (remove message_id; para o task se não houver mais nenhum)."""
        set_countdown_message_id(guild_id, cd_id, None)
        if not self._has_any_active_countdown(guild_id) and guild_id in self._countdown_tasks:
            self._countdown_tasks[guild_id].cancel()
            del self._countdown_tasks[guild_id]
        await interaction.response.send_message("✅ Countdown desta sala parado.", ephemeral=True)

    def _ensure_rcon_deps(self) -> str | None:
        """Garante que websockets está instalado (RCON usa só websockets). Retorna None se OK."""
        try:
            import websockets  # noqa: F401
        except ImportError:
            return "O pacote `websockets` não está instalado. Rode: pip install websockets"
        return None

    async def _fetch_rcon_info(self, guild_id: str, interaction: discord.Interaction):
        """Conecta via RCON em todos os servidores e busca nome, jogadores online e localização pelo IP."""
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers", [])
        if not servers:
            return await interaction.response.send_message("❌ Adicione servidores RCON primeiro.", ephemeral=True)
        err = self._ensure_rcon_deps()
        if err:
            return await interaction.response.send_message(f"❌ {err}", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        lines = []
        try:
            import aiohttp
            for s in servers:
                host = s.get("host", "")
                port = s.get("port", 28016)
                pwd = s.get("password", "")
                try:
                    info = await self._rcon_fetch_info(host, port, pwd)
                    region = await self._get_region_from_ip(host)
                    if info.get("hostname"):
                        s["detected_name"] = info["hostname"][:50]
                    game_port = info.get("game_port") if isinstance(info.get("game_port"), int) and 1 <= info.get("game_port", 0) <= 65535 else s.get("game_port", 28015)
                    s["game_port"] = game_port
                    if not s.get("connect_url") and host:
                        s["connect_url"] = f"steam://connect/{host}:{game_port}"
                    if region:
                        s["region"] = region
                    name = _server_display_name(s)
                    line = f"**{name}** — {info.get('players', '?')} online"
                    if region:
                        line += f" | 🌍 {region}"
                    lines.append(line)
                except Exception as e:
                    lines.append(f"**{_server_display_name(s)}** — ❌ Erro: {str(e)[:50]}")
            save_wipe_config(guild_id, cfg)
            msg = "\n".join(lines) if lines else "Nenhum servidor respondendo."
            await interaction.followup.send(f"🔍 **Info RCON:**\n{msg}", ephemeral=True)
            if lines:
                await self._send_rcon_log_embed(
                    guild_id,
                    "🔍 Verificação RCON — Configuração",
                    "Resultado da verificação ao usar «Buscar info RCON». Nome do servidor e jogadores online.",
                    lines,
                    color=0x5865F2,
                )
        except ImportError:
            await interaction.followup.send(
                "❌ Falta o pacote **websockets**. No servidor, rode: `pip install websockets` e reinicie o bot.",
                ephemeral=True,
            )

    def _get_rcon_log_channel(self, guild_id: str):
        """Retorna o canal de log RCON (ou startup) da guild, ou None."""
        ch_id = get_log_channel_id(guild_id, "rcon") or get_guild_config(guild_id).get("bot_log_channel_id")
        if not ch_id:
            return None
        guild = self.bot.get_guild(int(guild_id))
        if not guild:
            return None
        channel = guild.get_channel(int(ch_id))
        if not channel or not isinstance(channel, discord.TextChannel):
            return None
        return channel

    async def _send_rcon_log_embed(self, guild_id: str, title: str, description: str, lines: list, color: int = 0x2ECC71) -> bool:
        """Envia um embed de log RCON para o canal configurado. Retorna True se enviou."""
        channel = self._get_rcon_log_channel(guild_id)
        if not channel:
            return False
        text = "\n".join(lines)[:1024] if lines else "—"
        embed = discord.Embed(
            title=title,
            description=description,
            color=color,
            timestamp=datetime.now(timezone.utc),
        )
        embed.add_field(name="Servidores", value=text, inline=False)
        embed.set_footer(text="Suporte Valley • RCON")
        try:
            await channel.send(embed=embed)
            return True
        except (discord.Forbidden, Exception):
            return False

    async def _check_and_log_new_rcon_server(self, guild_id: str, host: str, port: int, password: str, name: str) -> None:
        """Verifica RCON recém-adicionado, grava nome do servidor e connect automático (steam://connect/host:28015)."""
        if self._ensure_rcon_deps():
            return
        cfg = get_wipe_config(guild_id)
        servers = list(cfg.get("rcon_servers") or [])
        try:
            info = await self._rcon_fetch_info(host, port, password)
            region = await self._get_region_from_ip(host)
            hostname = (info.get("hostname") or "").strip() or "(nome não obtido)"
            players = info.get("players", "?")
            game_port = info.get("game_port") if isinstance(info.get("game_port"), int) and 1 <= info.get("game_port", 0) <= 65535 else 28015
            for i, s in enumerate(servers):
                if str(s.get("host")) == host and int(s.get("port") or 0) == port:
                    servers[i] = {
                        **s,
                        "detected_name": hostname[:100] if hostname != "(nome não obtido)" else None,
                        "game_port": game_port,
                        "connect_url": s.get("connect_url") or f"steam://connect/{host}:{game_port}",
                    }
                    break
            cfg["rcon_servers"] = servers
            save_wipe_config(guild_id, cfg)
            display = hostname if hostname != "(nome não obtido)" else name
            line = f"**{display}** — Nome: `{hostname[:50]}` | **{players}** online | Connect automático"
            if region:
                line += f" | 🌍 {region}"
            await self._send_rcon_log_embed(
                guild_id,
                "✅ RCON configurado",
                "Servidor adicionado. Nome e connect identificados automaticamente.",
                [line],
                color=0x2ECC71,
            )
        except Exception as e:
            await self._send_rcon_log_embed(
                guild_id,
                "⚠️ RCON adicionado — falha na conexão",
                "O servidor foi salvo, mas a verificação falhou. Confira host, porta e senha.",
                [f"**{name}** (`{host}:{port}`) — ❌ {str(e)[:80]}"],
                color=0xE67E22,
            )

    async def _send_rcon_status_log(self, guild_id: str) -> None:
        """Envia log de status RCON para o canal configurado (startup ou rcon). Chamado ao iniciar o bot."""
        if self._ensure_rcon_deps():
            return
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers", [])
        if not servers:
            return
        lines = []
        try:
            for s in servers:
                host = s.get("host", "")
                port = s.get("port", 28016)
                pwd = s.get("password", "")
                try:
                    info = await self._rcon_fetch_info(host, port, pwd)
                    region = await self._get_region_from_ip(host)
                    hostname = (info.get("hostname") or "").strip() or "(não obtido)"
                    name = _server_display_name(s)
                    line = f"**{name}** — `{hostname[:40]}` | {info.get('players', '?')} online"
                    if region:
                        line += f" | 🌍 {region}"
                    lines.append(line)
                except Exception as e:
                    lines.append(f"**{_server_display_name(s)}** — ❌ Erro: {str(e)[:50]}")
        except Exception as e:
            lines.append(f"Erro geral: {str(e)[:200]}")
        if not lines:
            return
        await self._send_rcon_log_embed(
            guild_id,
            "🖥️ Status RCON — Startup",
            "Verificação dos servidores RCON ao iniciar o bot.",
            lines,
            color=0x2ECC71,
        )

    async def _rust_rcon_command(self, host: str, port: int, password: str, command: str, timeout_sec: float = 2.5) -> str:
        """
        Envia um comando RCON ao servidor Rust (protocolo WebRcon).
        URI: ws://host:port/senha — payload JSON: Identifier, Message, Name.
        Modelo do bot de referência (bot para analisar).
        """
        import websockets
        uri = f"ws://{host}:{port}/{password}"
        payload = {"Identifier": 1, "Message": command, "Name": "WebRcon"}
        async with websockets.connect(uri, open_timeout=timeout_sec, close_timeout=timeout_sec) as ws:
            await ws.send(json.dumps(payload))
            end = time.time() + timeout_sec
            while time.time() < end:
                remaining = max(0.1, end - time.time())
                msg = await asyncio.wait_for(ws.recv(), timeout=remaining)
                data = json.loads(msg)
                if data.get("Identifier") == 1:
                    return data.get("Message") or ""
        return ""

    def _parse_playerlist(self, output: str) -> int:
        """Conta jogadores na saída do comando playerlist (Steam ID 64 ou linhas numéricas)."""
        if not output or not output.strip():
            return 0
        steam_ids = re.findall(r"\b7656\d{13}\b", output)
        if steam_ids:
            return len(set(steam_ids))
        lines = [l.strip() for l in output.splitlines() if l.strip()]
        ignore = ("players", "connected", "id ", "name", "steamid", "----", "total")
        cleaned = [l for l in lines if not any(w in l.lower() for w in ignore)]
        return max(0, sum(1 for l in cleaned if re.match(r"^\d+\s+", l)))

    def _parse_playerlist_entries(self, output: str) -> list[str]:
        """Extrai lista de nomes de jogadores da saída do playerlist (um por linha, para exibir no embed)."""
        if not output or not output.strip():
            return []
        lines = [l.strip() for l in output.splitlines() if l.strip()]
        ignore = ("players", "connected", "id ", "name", "steamid", "----", "total")
        names = []
        for line in lines:
            if any(w in line.lower() for w in ignore):
                continue
            if not re.match(r"^\d+\s+", line):
                continue
            tokens = line.split()
            steam_id = None
            for t in tokens:
                if re.fullmatch(r"7656\d{13}", t):
                    steam_id = t
                    break
            if steam_id:
                idx = line.find(steam_id)
                name_part = line[:idx].strip()
                parts = name_part.split(None, 1)
                name = parts[1] if len(parts) > 1 else parts[0] if parts else "?"
            else:
                parts = line.split(None, 1)
                name = parts[1] if len(parts) > 1 else parts[0] if parts else "?"
            if name and name != "?":
                names.append(name[:32])
        return names[:50]

    async def _get_game_port_from_rcon(self, host: str, port: int, password: str) -> int | None:
        """Tenta obter a porta do jogo (client connect) via RCON. Retorna None se não conseguir."""
        for cmd in ("server.port", "server.port ", "port"):
            try:
                out = await self._rust_rcon_command(host, port, password, cmd, timeout_sec=2.0)
                if not out:
                    continue
                # Procura um número que seja porta válida (ex.: 28015, 26684)
                for m in re.finditer(r"\b(2[0-9]{4}|[3-9]\d{4}|1\d{4})\b", (out or "").strip()):
                    p = int(m.group(1))
                    if 1024 <= p <= 65535:
                        return p
                # Último recurso: qualquer número 1-65535
                for m in re.finditer(r"\b(\d{1,5})\b", (out or "").strip()):
                    p = int(m.group(1))
                    if 1 <= p <= 65535:
                        return p
            except Exception:
                continue
        return None

    async def _rcon_fetch_info(self, host: str, port: int, password: str) -> dict:
        """Busca server hostname, player count e porta do jogo (para connect) via WebRcon."""
        try:
            timeout = 2.5
            out_hostname = await self._rust_rcon_command(host, port, password, "server.hostname", timeout)
            # Normaliza saída, removendo prefixos como "server.hostname:" para exibir só o nome.
            hostname_clean = self._normalize_hostname(out_hostname)
            out_players = await self._rust_rcon_command(host, port, password, "playerlist", timeout)
            game_port = await self._get_game_port_from_rcon(host, port, password)
            result = {
                "hostname": hostname_clean[:100] if hostname_clean else "",
                "players": str(self._parse_playerlist(out_players or "")),
                "game_port": game_port,
            }
            return result
        except Exception as e:
            raise RuntimeError(str(e)[:80]) from e

    async def _rcon_fetch_full_info(self, host: str, port: int, password: str) -> dict:
        """Busca hostname, quantidade de jogadores e lista de nomes (para embed com jogador por jogador)."""
        try:
            timeout = 2.5
            out_hostname = await self._rust_rcon_command(host, port, password, "server.hostname", timeout)
            hostname_clean = self._normalize_hostname(out_hostname)
            out_players = await self._rust_rcon_command(host, port, password, "playerlist", timeout)
            hostname = hostname_clean[:100] if hostname_clean else ""
            count = self._parse_playerlist(out_players or "")
            names = self._parse_playerlist_entries(out_players or "")
            return {"hostname": hostname, "players": count, "player_names": names}
        except Exception as e:
            return {"hostname": "", "players": 0, "player_names": [], "error": str(e)[:80]}

    async def _get_region_from_ip(self, host: str) -> str | None:
        """Obtém região/timezone do IP via ip-api.com (free, no key)."""
        try:
            ip = socket.gethostbyname(host)
        except Exception:
            return None
        try:
            import aiohttp
            async with aiohttp.ClientSession() as session:
                async with session.get(f"http://ip-api.com/json/{ip}?fields=countryCode,timezone", timeout=aiohttp.ClientTimeout(total=5)) as r:
                    if r.status == 200:
                        data = await r.json()
                        tz = data.get("timezone", "")
                        cc = data.get("countryCode", "")
                        if tz:
                            if "America" in tz or cc in ("BR", "US", "CA"):
                                return "US/BR"
                            if "Europe" in tz or cc in ("DE", "FR", "UK", "NL"):
                                return "EU"
                        return cc or None
        except Exception:
            pass
        return None

    async def _get_timezone_from_ip(self, host: str) -> str | None:
        """Obtém o nome do fuso horário (ex: America/Sao_Paulo) via ip-api.com para exibir wipe no horário local."""
        try:
            ip = socket.gethostbyname(host)
        except Exception:
            return None
        try:
            import aiohttp
            async with aiohttp.ClientSession() as session:
                async with session.get(f"http://ip-api.com/json/{ip}?fields=timezone,countryCode", timeout=aiohttp.ClientTimeout(total=5)) as r:
                    if r.status == 200:
                        data = await r.json()
                        tz = (data.get("timezone") or "").strip()
                        if tz:
                            return tz
                        cc = data.get("countryCode", "")
                        if cc == "BR":
                            return "America/Sao_Paulo"
                        if cc in ("US", "CA"):
                            return "America/New_York"
                        if cc in ("DE", "FR", "GB", "NL"):
                            return "Europe/London"
                        return None
        except Exception:
            pass
        return None

    def _format_wipe_local(self, target_utc: datetime | None, tz_name: str | None) -> str:
        """Formata a data do wipe no fuso local (ex: 25/02 15:00 (Brasília))."""
        if not target_utc or not tz_name:
            return ""
        try:
            from zoneinfo import ZoneInfo
            local = target_utc.astimezone(ZoneInfo(tz_name))
            labels = {
                "America/Sao_Paulo": "Brasília (BR)",
                "Europe/London": "Londres (EU)",
                "Europe/Paris": "Paris (EU)",
                "Europe/Berlin": "Berlim (EU)",
                "America/New_York": "Nova York (US)",
                "America/Chicago": "Chicago (US)",
                "America/Los_Angeles": "Los Angeles (US)",
            }
            label = labels.get(tz_name) or tz_name.split("/")[-1].replace("_", " ")
            return f"{local.strftime('%d/%m %H:%M')} ({label})"
        except Exception:
            return target_utc.strftime("%d/%m %H:%M") + " UTC"

    def _normalize_hostname(self, raw: str | None) -> str:
        """
        Remove prefixos técnicos da saída de server.hostname (ex.: 'server.hostname: "Nome"')
        para exibir apenas o nome limpo do servidor.
        """
        s = (raw or "").strip()
        if not s:
            return ""
        lower = s.lower()
        prefix = "server.hostname"
        if lower.startswith(prefix):
            sep_idx = -1
            for ch in (":", "="):
                idx = s.find(ch)
                if idx != -1:
                    sep_idx = idx
                    break
            if sep_idx != -1:
                s = s[sep_idx + 1 :].lstrip()
            else:
                s = s[len(prefix) :].lstrip(" :=")
        return s.strip(' "')

    async def _stop_countdown(self, guild_id: str, interaction: discord.Interaction):
        if guild_id in self._countdown_tasks:
            self._countdown_tasks[guild_id].cancel()
            del self._countdown_tasks[guild_id]
        cfg = get_wipe_config(guild_id)
        cfg["countdown_message_id"] = None
        save_wipe_config(guild_id, cfg)
        await interaction.response.send_message("✅ Countdown parado.", ephemeral=True)

    def _build_embed_for_countdown(
        self,
        guild_id: str,
        cd: dict,
        full_info: dict,
        target_dt: datetime | None,
        local_wipe_str: str | None = None,
    ) -> discord.Embed | None:
        """Monta a embed de um countdown por sala (nome do servidor, jogadores, opções, horário local)."""
        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        opts = cd.get("embed_options") or DEFAULT_EMBED_OPTIONS
        lang = (cd.get("lang") or "pt").lower()
        labels = {
            "pt": {
                "default_server": "Servidor",
                "countdown_title": "⏱️ Countdown Wipe",
                "local_time": "🕐 Horário local (região do servidor)",
                "players_online": "Jogadores online",
                "player_list": "Lista de jogadores",
                "more_suffix": "mais",
                "footer": "Atualiza a cada minuto",
            },
            "en": {
                "default_server": "Server",
                "countdown_title": "⏱️ Wipe Countdown",
                "local_time": "🕐 Local time (server region)",
                "players_online": "Players online",
                "player_list": "Player list",
                "more_suffix": "more",
                "footer": "Updates every minute",
            },
        }.get(lang, {
            "default_server": "Servidor",
            "countdown_title": "⏱️ Countdown Wipe",
            "local_time": "🕐 Horário local (região do servidor)",
            "players_online": "Jogadores online",
            "player_list": "Lista de jogadores",
            "more_suffix": "mais",
            "footer": "Atualiza a cada minuto",
        })
        hostname = (full_info.get("hostname") or "").strip() or labels["default_server"]
        title = hostname if opts.get("show_server_name", True) else labels["countdown_title"]
        embed = discord.Embed(
            title=title[:256],
            color=color,
            timestamp=datetime.utcnow(),
        )
        if opts.get("show_wipe_countdown", True) and target_dt:
            embed.description = _format_countdown(target_dt, lang=lang)
        if opts.get("show_wipe_countdown", True) and local_wipe_str:
            embed.add_field(name=labels["local_time"], value=local_wipe_str, inline=False)
        if opts.get("show_player_count", True):
            count = full_info.get("players", 0)
            embed.add_field(name=labels["players_online"], value=str(count), inline=True)
        if opts.get("show_player_list", True):
            names = full_info.get("player_names") or []
            # Só mostra a lista se houver ao menos um jogador; caso contrário, não exibe o campo.
            if names:
                text = "\n".join(f"• {n}" for n in names[:25])
                if len(names) > 25:
                    text += f"\n*+{len(names) - 25} {labels['more_suffix']}*"
                embed.add_field(name=labels["player_list"], value=text[:1024] or "—", inline=False)
        if opts.get("show_banner", True) and cd.get("banner_url"):
            embed.set_image(url=cd["banner_url"])
        interval = max(5, int(cd.get("update_interval_seconds") or 60))
        if lang == "en":
            embed.set_footer(text=f"Updates every {interval}s")
        else:
            embed.set_footer(text=f"Atualiza a cada {interval}s")
        return embed

    async def _countdown_loop(self, guild_id: str):
        """Atualiza countdown legado (um por guild) e todos os countdowns por sala que tiverem message_id."""
        cfg = get_wipe_config(guild_id)
        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        rcon_servers = cfg.get("rcon_servers") or []
        # Controle de frequência por painel (respeita update_interval_seconds de cada countdown).
        last_update_by_cd: dict[str, float] = {}
        last_legacy_update: float = 0.0

        while True:
            try:
                await asyncio.sleep(5)
                guild = self.bot.get_guild(int(guild_id))
                if not guild:
                    break

                # Countdown legado (um canal, uma mensagem)
                ch_id = cfg.get("countdown_channel_id")
                msg_id = cfg.get("countdown_message_id")
                dt_utc = cfg.get("wipe_datetime_utc")
                now_ts = time.time()
                if ch_id and msg_id and dt_utc and (now_ts - last_legacy_update >= 60):
                    try:
                        target = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
                        if target.tzinfo is None:
                            target = target.replace(tzinfo=timezone.utc)
                        channel = guild.get_channel(int(ch_id))
                        if channel:
                            msg = await channel.fetch_message(int(msg_id))
                            embed = discord.Embed(
                                title="⏱️ COUNTDOWN PARA O WIPE",
                                description=_format_countdown(target),
                                color=color,
                                timestamp=datetime.utcnow(),
                            )
                            if cfg.get("banner_url"):
                                embed.set_image(url=cfg["banner_url"])
                            embed.set_footer(text="Atualiza a cada minuto")
                            await msg.edit(embed=embed)
                            last_legacy_update = now_ts
                    except Exception as e:
                        print(f"[Wipe] Erro countdown legado {guild_id}: {e}")

                # Countdowns por sala
                for cd in list_countdowns(guild_id):
                    if not cd.get("message_id"):
                        continue
                    cd_ch_id = cd.get("channel_id")
                    rcon_idx = cd.get("rcon_index", 0)
                    cd_dt = cd.get("wipe_datetime_utc")
                    if not cd_ch_id or rcon_idx < 0 or rcon_idx >= len(rcon_servers):
                        continue
                    cd_id = str(cd.get("id") or "")
                    interval = max(5, int(cd.get("update_interval_seconds") or 60))
                    last_ts = float(last_update_by_cd.get(cd_id, 0.0))
                    if (now_ts - last_ts) < interval:
                        continue
                    target_dt = None
                    try:
                        if cd_dt:
                            target_dt = datetime.fromisoformat(cd_dt.replace("Z", "+00:00"))
                            if target_dt.tzinfo is None:
                                target_dt = target_dt.replace(tzinfo=timezone.utc)
                    except Exception:
                        target_dt = None
                    srv = rcon_servers[rcon_idx]
                    host = srv.get("host") or ""
                    port = int(srv.get("port") or 28016)
                    pw = srv.get("password") or ""
                    full_info = await self._rcon_fetch_full_info(host, port, pw)
                    tz_name = await self._get_timezone_from_ip(host)
                    local_wipe_str = self._format_wipe_local(target_dt, tz_name) if target_dt else ""
                    channel = guild.get_channel(int(cd_ch_id))
                    if not channel:
                        continue
                    msg = await channel.fetch_message(int(cd["message_id"]))
                    embed = self._build_embed_for_countdown(guild_id, cd, full_info, target_dt, local_wipe_str=local_wipe_str or None)
                    if embed:
                        await msg.edit(embed=embed)
                        last_update_by_cd[cd_id] = now_ts
                # Recarregar config após possíveis alterações
                cfg = get_wipe_config(guild_id)
                rcon_servers = cfg.get("rcon_servers") or []
            except asyncio.CancelledError:
                break
            except Exception as e:
                print(f"[Wipe] Erro countdown loop {guild_id}: {e}")
                await asyncio.sleep(60)


async def setup(bot):
    await bot.add_cog(WipeCog(bot))
