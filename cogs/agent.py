"""
Agente Pessoal - Supervisão, treinamento e ações em canais.
- Definir salas e categorias para supervisionar
- Treinar com regras (trigger → ação)
- Instruções para IA responder dúvidas da comunidade
- Opção de atuar em tickets
"""
import re
from datetime import datetime

import discord
from discord.ext import commands
from discord.enums import ChannelType
from discord.ui import ChannelSelect, Modal, TextInput

from config import BOT_OWNER_ID, OPENAI_API_KEY, GROQ_API_KEY
from utils.storage import get_guild_config, save_guild_config, get_ticket_by_channel
from utils.panel_cache import get_last_panel_message_id, set_last_panel_message_id
from utils.toxic import might_be_toxic, get_toxic_alert
from utils.translator import detect_language
from utils.limits import (
    MAX_LEARNED_DATA,
    MAX_LEARNED_ITEM_CONTENT,
    MAX_TEACHINGS,
    MAX_AGENT_CHANNELS,
    MAX_AGENT_CATEGORIES,
)


DEFAULT_INSTRUCTIONS = """Você é a melhor agente de IA de suporte de comunidade. Seu papel é identificar DÚVIDAS REAIS dos jogadores.

REGRAS:
1. NÃO responda conversas normais, cumprimentos, brincadeiras, comentários gerais ou opiniões.
2. Responda APENAS quando o jogador tiver uma DÚVIDA ou PERGUNTA clara (ex: "como faço X?", "where is Y?", "não consigo...", "problema com...", "help").
3. Se souber a resposta: responda de forma objetiva e curta.
4. Se NÃO souber: diga para abrir ticket e mencione o canal de tickets usando <#ID_DO_CANAL> (o ID estará no contexto).
5. Seja breve. Máximo 2-3 frases.
6. Use <#ID> para canais, <@ID> para usuários, <@&ID> para cargos."""


def can_use_sup(user_id: str, guild_id: str) -> bool:
    if str(user_id) == BOT_OWNER_ID:
        return True
    config = get_guild_config(guild_id)
    return str(user_id) in config.get("allowed_sup_users", [])


def color_from_hex(hex_color: str) -> int:
    h = hex_color.lstrip("#")
    return int(h, 16) if h else 0x5865F2


def is_channel_supervised(channel: discord.TextChannel, config: dict) -> bool:
    """Verifica se o canal está na lista de supervisão."""
    if not config.get("agent_enabled"):
        return False
    ch_id = str(channel.id)
    cat_id = str(channel.category_id) if channel.category_id else None

    if ch_id in config.get("agent_channels", []):
        return True
    if cat_id and cat_id in config.get("agent_categories", []):
        return True
    return False


def is_ticket_channel_supervised(channel_id: str, config: dict) -> bool:
    """Verifica se tickets devem ser supervisionados."""
    return config.get("agent_enabled") and config.get("agent_in_tickets")


class AgentCog(commands.Cog):
    """Cog do Agente Pessoal."""

    def __init__(self, bot):
        self.bot = bot

    def _build_agent_embed(self, guild_id: str) -> discord.Embed:
        config = get_guild_config(guild_id)
        channels = config.get("agent_channels", [])
        categories = config.get("agent_categories", [])
        teachings = config.get("agent_teachings", [])
        enabled = config.get("agent_enabled", False)
        in_tickets = config.get("agent_in_tickets", False)

        embed = discord.Embed(
            title="🤖 Agente Pessoal",
            description="Configure o agente para supervisionar canais e executar ações.",
            color=0x5865F2,
            timestamp=datetime.utcnow(),
        )
        embed.add_field(
            name="⚙️ Status",
            value=f"**Ativo:** {'✅' if enabled else '❌'}\n**Em tickets:** {'✅' if in_tickets else '❌'}",
            inline=True,
        )
        embed.add_field(
            name="📺 Canais",
            value=", ".join(f"<#{c}>" for c in channels[:5]) or "`Nenhum`",
            inline=True,
        )
        embed.add_field(
            name="📁 Categorias",
            value=", ".join(f"<#{c}>" for c in categories[:5]) or "`Nenhum`",
            inline=True,
        )
        embed.add_field(
            name="📚 Ensino (regras)",
            value=f"**{len(teachings)}** regras configuradas",
            inline=True,
        )
        embed.add_field(
            name="📋 Log",
            value=f"<#{config.get('agent_log_channel')}>" if config.get("agent_log_channel") else "`Não definido`",
            inline=True,
        )
        train_ch = config.get("agent_training_channel")
        embed.add_field(
            name="📖 Canal de Treino",
            value=f"<#{train_ch}>" if train_ch else "`Não definido`",
            inline=True,
        )
        learned = config.get("agent_learned_data", [])
        embed.add_field(
            name="🧠 Aprendizados",
            value=f"**{len(learned)}** itens na memória",
            inline=True,
        )
        trans = config.get("ticket_translation_enabled", False)
        embed.add_field(
            name="🌐 Tradução em Tickets",
            value="✅ Ativada" if trans else "❌ Desativada",
            inline=True,
        )
        instructions = config.get("agent_instructions", "")
        ai_on = config.get("agent_ai_enabled", False)
        has_api = bool(OPENAI_API_KEY or GROQ_API_KEY)
        embed.add_field(
            name="🧠 Instruções da IA",
            value=f"{'✅ Configuradas' if instructions else '❌ Não definidas'}\n**IA ativa:** {'✅' if ai_on else '❌'}\n**API:** {'✅' if has_api else '❌ (.env)'}",
            inline=True,
        )
        embed.set_footer(text="Use os botões abaixo para configurar")

        return embed

    async def _process_training_channel(self, message: discord.Message, config: dict) -> bool:
        """Se for canal de treino: armazena, envia confirmação e apaga a mensagem."""
        train_ch = config.get("agent_training_channel")
        if not train_ch or str(message.channel.id) != str(train_ch):
            return False
        if not can_use_sup(str(message.author.id), str(message.guild.id)):
            return False
        content = (message.content or "").strip()
        if not content:
            return False
        learned = config.get("agent_learned_data", [])
        learned.append({
            "content": content[:MAX_LEARNED_ITEM_CONTENT],
            "author_id": str(message.author.id),
            "created_at": datetime.utcnow().isoformat(),
        })
        config["agent_learned_data"] = learned[-MAX_LEARNED_DATA:]
        save_guild_config(str(message.guild.id), config)
        try:
            await message.delete()
        except discord.Forbidden:
            pass
        try:
            await message.channel.send(
                f"✅ {message.author.mention} **Treinamento recebido.** A IA armazenou a informação.",
                delete_after=8,
            )
        except discord.Forbidden:
            pass
        return True

    @commands.Cog.listener()
    async def on_message(self, message: discord.Message):
        """Processa mensagens nos canais supervisionados e canal de treino."""
        if message.author.bot:
            return

        config = get_guild_config(str(message.guild.id))
        if await self._process_training_channel(message, config):
            return
        channel = message.channel

        # Verifica se está em canal supervisionado ou ticket
        is_supervised = is_channel_supervised(channel, config)
        ticket = get_ticket_by_channel(str(channel.id))
        if ticket and is_ticket_channel_supervised(str(channel.id), config):
            is_supervised = True

        if not is_supervised:
            return

        content = (message.content or "").lower()
        raw_content = message.content or ""

        # Anti-toxic: pré-filtro + IA para contexto (entende ofensa vs expressão)
        is_toxic = False
        if might_be_toxic(raw_content):
            ai_result = await self._is_toxic_ai(raw_content)
            if ai_result is True:
                is_toxic = True
            elif ai_result is None:
                is_toxic = True  # Erro na API: conservador, remove
        if is_toxic:
            try:
                await message.delete()
                lang = detect_language(raw_content)
                if lang == "unknown":
                    lang = "en"
                alert = get_toxic_alert(lang)
                warn_msg = await channel.send(
                    f"⚠️ {message.author.mention} {alert}",
                    delete_after=10,
                )
                if config.get("agent_log_channel"):
                    log_ch = message.guild.get_channel(int(config["agent_log_channel"]))
                    if log_ch:
                        log_embed = discord.Embed(
                            title="🚫 Anti-toxic — Mensagem removida",
                            description=f"**Canal:** {channel.mention}\n**Usuário:** {message.author}",
                            color=0xE74C3C,
                            timestamp=datetime.utcnow(),
                        )
                        log_embed.add_field(name="Conteúdo", value=raw_content[:400], inline=False)
                        await log_ch.send(embed=log_embed)
            except discord.Forbidden:
                pass
            return

        teachings = config.get("agent_teachings", [])

        for t in teachings:
            trigger = t.get("trigger", "").lower()
            action = t.get("action", "log")
            response = t.get("response", "Mensagem violou as regras.")

            if not trigger:
                continue

            is_regex = t.get("regex", False)
            matched = False
            if is_regex:
                try:
                    matched = bool(re.search(trigger, message.content or "", re.IGNORECASE))
                except re.error:
                    matched = trigger in content
            else:
                matched = trigger in content

            if not matched:
                continue

            try:
                if action in ("delete", "delete_warn"):
                    await message.delete()

                if action in ("warn", "delete_warn"):
                    warn_msg = await channel.send(
                        f"⚠️ {message.author.mention} {response}",
                        delete_after=10,
                    )
                    if action == "warn":
                        await message.delete(delay=2)

                if action in ("log", "delete", "delete_warn", "warn") and config.get("agent_log_channel"):
                    log_ch = message.guild.get_channel(int(config["agent_log_channel"]))
                    if log_ch:
                        log_embed = discord.Embed(
                            title="📋 Agente — Ação executada",
                            description=f"**Canal:** {channel.mention}\n**Usuário:** {message.author}\n**Trigger:** `{trigger}`\n**Ação:** {action}",
                            color=0xE74C3C,
                            timestamp=datetime.utcnow(),
                        )
                        log_embed.add_field(name="Mensagem", value=(message.content or "_(mídia)_")[:500], inline=False)
                        await log_ch.send(embed=log_embed)

                if action == "reply":
                    await channel.send(response)

                break
            except discord.Forbidden:
                break

        # Se nenhuma regra disparou e a IA está ativa: detecta dúvida e responde
        has_llm = bool(OPENAI_API_KEY or GROQ_API_KEY)
        if has_llm and config.get("agent_instructions") and config.get("agent_ai_enabled"):
            try:
                if not await self._is_doubt_or_question(message.content or "", config):
                    return
                response_text = await self._ask_ai_response(message, config)
                if response_text and len(response_text.strip()) > 0:
                    await channel.send(response_text[:1900], delete_after=60)
            except Exception:
                pass

    async def _is_doubt_or_question(self, text: str, config: dict | None = None) -> bool:
        """
        Detecta se a mensagem é uma DÚVIDA ou PERGUNTA real.
        Se doubt_use_ai_validation=False na config, usa só padrões (mais permissivo).
        """
        config = config or {}
        if not text or len(text.strip()) < 3:
            return False
        t = text.lower().strip()
        # Padrões amplos: qualquer um indica possível dúvida (PT-BR e EN)
        doubt_patterns = [
            "como", "how", "where", "onde", "qual", "which", "what", "quando", "when",
            "por que", "why", "porque", "por quê", "pq ", " por q",
            "não consigo", "cant", "can't", "cannot", "não sei", "don't know", "dunno",
            "dúvida", "doubt", "question", "pergunta", "help", "ajuda", "ajudar", "ajude",
            "problema", "problem", "error", "erro", "bug", "não funciona", "doesn't work", "won't work",
            "?", "help me", "me ajuda", "me help", "alguém sabe", "anyone know", "someone help",
            "como faço", "how do i", "how to", "como que", "como funciona", "how does",
            "cadê", "cade", "where is", "onde está", "where's",
            "preciso de ajuda", "preciso help", "i need help", "need help", "preciso saber",
            "não entendi", "não entendo", "don't understand", "confuso", "confused",
            "não abre", "não carrega", "not loading", "won't load", "não inicia",
            "travou", "trava", "travando", "crash", "crashing", "freeze", "freezing",
            "alguém pode", "alguém me", "someone can", "anyone can", "alguém explica",
            "tem como", "dá pra", "da pra", "dá para", "da para", "is there a way",
            "quero saber", "want to know", "wanna know", "gostaria de saber",
            "não aparece", "doesn't show", "sumiu", "disappeared", "perdi", "lost my",
            "fix", "consertar", "resolver", "solve", "funciona", "working", "work ",
            "tutorial", "guide", "guia", "passo a passo", "step by step",
            "o que é", "what is", "what's", "o que fazer", "what to do",
            "será que", "sera que", "me expliqu", "explain", "explica",
            "instal", "install", "baixar", "download", "conectar", "connect",
            "login", "entrar", "registr", "account", "conta",
        ]
        matched = any(p in t for p in doubt_patterns)
        # Também aceita mensagens que terminam com ? (pergunta direta) e têm conteúdo razoável
        if not matched and "?" in t and len(t) >= 5:
            matched = True
        if not matched:
            return False
        # Se validação por IA desativada: confia nos padrões (mais permissivo)
        if not config.get("doubt_use_ai_validation", True):
            return True
        if not (OPENAI_API_KEY or GROQ_API_KEY):
            return True
        system = """Você analisa se a mensagem de um jogador é uma DÚVIDA ou pedido de AJUDA real.

É DÚVIDA (responda SIM): pedido de ajuda, pergunta sobre como fazer algo, problema técnico, "não consigo", "como faço", travou, bug, erro, não funciona, não entendi, cadê, onde está, preciso de ajuda, etc.

NÃO é dúvida (responda NAO): APENAS cumprimento ("oi", "e aí"), brincadeira sem pedir ajuda, comentário de opinião ("achei legal"), afirmação sem pergunta.

IMPORTANTE: Na dúvida, responda SIM. Só responda NAO se for CLARAMENTE conversa casual sem pedido de ajuda.
Responda APENAS: SIM ou NAO"""
        if OPENAI_API_KEY:
            try:
                from openai import OpenAI
                client = OpenAI(api_key=OPENAI_API_KEY)
                resp = client.chat.completions.create(
                    model="gpt-4o-mini",
                    messages=[{"role": "system", "content": system}, {"role": "user", "content": text[:300]}],
                    max_tokens=5,
                )
                r = (resp.choices[0].message.content or "").strip().upper()
                return "SIM" in r or "YES" in r
            except Exception:
                return True
        if GROQ_API_KEY:
            try:
                from groq import Groq
                client = Groq(api_key=GROQ_API_KEY)
                resp = client.chat.completions.create(
                    model="llama-3.3-70b-versatile",
                    messages=[{"role": "system", "content": system}, {"role": "user", "content": text[:300]}],
                    max_tokens=5,
                )
                r = (resp.choices[0].message.content or "").strip().upper()
                return "SIM" in r or "YES" in r
            except Exception:
                return True
        return True

    async def _is_toxic_ai(self, text: str) -> bool | None:
        """
        IA analisa o contexto: ofensa real vs expressão/brincadeira.
        Retorna True=tóxico, False=OK, None=erro.
        """
        if not text or len(text.strip()) < 2:
            return False
        system = """Você é um moderador de comunidade de jogos. Analise SE a mensagem é OFENSIVA no contexto.

CONSIDERE:
- Insulto direto a alguém = TÓXICO
- Palavrão dirigido a pessoa (ex: "você é um X") = TÓXICO
- Slurs, discriminação, ameaças = TÓXICO
- Xingar situação/jogo (ex: "que merda de lag", "this game is shit") = OK
- Brincadeira entre amigos sem insulto real = OK
- Expressões comuns (ex: "caramba", "putz") = OK

Responda APENAS: TÓXICO ou OK"""

        user_msg = f"Mensagem: {text[:500]}"

        if OPENAI_API_KEY:
            try:
                from openai import OpenAI
                client = OpenAI(api_key=OPENAI_API_KEY)
                resp = client.chat.completions.create(
                    model="gpt-4o-mini",
                    messages=[{"role": "system", "content": system}, {"role": "user", "content": user_msg}],
                    max_tokens=10,
                )
                r = (resp.choices[0].message.content or "").strip().upper()
                return "TÓXICO" in r or "TOXIC" in r
            except Exception:
                return None
        if GROQ_API_KEY:
            try:
                from groq import Groq
                client = Groq(api_key=GROQ_API_KEY)
                resp = client.chat.completions.create(
                    model="llama-3.3-70b-versatile",
                    messages=[{"role": "system", "content": system}, {"role": "user", "content": user_msg}],
                    max_tokens=10,
                )
                r = (resp.choices[0].message.content or "").strip().upper()
                return "TÓXICO" in r or "TOXIC" in r
            except Exception:
                return None
        return None

    async def _ask_ai_response(self, message: discord.Message, config: dict) -> str | None:
        """Chama a IA (OpenAI ou Groq) com as instruções e contexto do servidor."""
        guild = message.guild
        instructions = config.get("agent_instructions", "").strip()
        if not instructions:
            return None

        # Monta contexto: canais, categoria de tickets, cargo suporte
        ctx_lines = ["**Contexto do servidor Discord:**"]
        ctx_lines.append(f"- Servidor: {guild.name}")
        if config.get("category_id"):
            cat = guild.get_channel(int(config["category_id"]))
            if cat:
                ctx_lines.append(f"- Categoria de tickets: {cat.name} (<#{config['category_id']}>)")
        if config.get("support_role_id"):
            ctx_lines.append(f"- Cargo de suporte: <@&{config['support_role_id']}>")
        for ch in list(guild.text_channels)[:15]:
            ctx_lines.append(f"- Canal: {ch.name} (<#{ch.id}>)")
        context = "\n".join(ctx_lines)

        ticket_ctx = ""
        tc_id = config.get("agent_ticket_channel")
        if tc_id:
            ticket_ctx = f"\nCanal de tickets: <#{tc_id}> (mencione ao orientar abrir ticket)"
        else:
            ticket_chs = [c for c in guild.text_channels if "ticket" in c.name.lower() or "suporte" in c.name.lower() or "support" in c.name.lower()]
            if ticket_chs:
                ticket_ctx = f"\nCanal de tickets: <#{ticket_chs[0].id}> (mencione ao orientar abrir ticket)"
        if not ticket_ctx and config.get("category_id"):
            cat = guild.get_channel(int(config["category_id"]))
            if cat:
                ticket_ctx = f"\nCategoria de tickets: {cat.name} (informe ao jogador para abrir ticket)"
        system = f"""{instructions}

{context}
{ticket_ctx}

IMPORTANTE: Responda APENAS se for uma dúvida real. Seja objetiva. Se não souber, oriente a abrir ticket e mencione o canal de tickets.
Use <#ID> para canais, <@ID> para usuários, <@&ID> para cargos.
Se não for dúvida ou não tiver resposta útil, responda APENAS: NENHUMA_RESPOSTA"""

        user_msg = f"Mensagem de {message.author.display_name} em #{message.channel.name}:\n{message.content or '(sem texto)'}"

        # Tenta OpenAI primeiro, depois Groq (gratuito)
        if OPENAI_API_KEY:
            try:
                from openai import OpenAI
                client = OpenAI(api_key=OPENAI_API_KEY)
                resp = client.chat.completions.create(
                    model="gpt-4o-mini",
                    messages=[
                        {"role": "system", "content": system},
                        {"role": "user", "content": user_msg},
                    ],
                    max_tokens=300,
                )
                text = (resp.choices[0].message.content or "").strip()
                if text.upper() == "NENHUMA_RESPOSTA" or not text:
                    return None
                return text
            except Exception:
                pass

        if GROQ_API_KEY:
            try:
                from groq import Groq
                client = Groq(api_key=GROQ_API_KEY)
                resp = client.chat.completions.create(
                    model="llama-3.3-70b-versatile",
                    messages=[
                        {"role": "system", "content": system},
                        {"role": "user", "content": user_msg},
                    ],
                    max_tokens=300,
                )
                text = (resp.choices[0].message.content or "").strip()
                if text.upper() == "NENHUMA_RESPOSTA" or not text:
                    return None
                return text
            except Exception:
                pass

        return None

    @commands.command(name="agent")
    async def agent_command(self, ctx: commands.Context):
        """Atalho para !sup → Agente. Abre o painel do Agente."""
        if not can_use_sup(str(ctx.author.id), str(ctx.guild.id)):
            return await ctx.send("❌ Sem permissão.", delete_after=5)

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

        embed = self._build_agent_embed(str(ctx.guild.id))
        view = AgentConfigView(self.bot, str(ctx.guild.id), self._build_agent_embed)
        msg = await ctx.send(embed=embed, view=view)
        set_last_panel_message_id(ctx.channel.id, msg.id)


class AgentConfigView(discord.ui.View):
    """View principal do Agente — menu por categorias."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        placeholder="📂 Selecione a categoria de configuração...",
        options=[
            discord.SelectOption(label="Dúvidas", value="duvidas", emoji="❓", description="Canais, detecção e IA de dúvidas"),
            discord.SelectOption(label="Supervisão", value="supervisao", emoji="📺", description="Canais e categorias"),
            discord.SelectOption(label="Regras", value="regras", emoji="📚", description="Ensinar, ver e remover regras"),
            discord.SelectOption(label="Instruções e IA", value="ia", emoji="🧠", description="Instruções e respostas da IA"),
            discord.SelectOption(label="Canal de Treino", value="treino", emoji="📖", description="Onde o agente aprende"),
            discord.SelectOption(label="Log e Tickets", value="log", emoji="📋", description="Log, tradução, tickets"),
            discord.SelectOption(label="Status", value="status", emoji="⚡", description="Ligar/desligar agente"),
        ],
        row=0,
    )
    async def select_category(self, interaction: discord.Interaction, select: discord.ui.Select):
        value = select.values[0] if select.values else ""
        if value == "duvidas":
            view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
            embed = view._build_doubt_embed()
            await interaction.response.edit_message(embed=embed, view=view)
        elif value == "supervisao":
            view = AgentSupervisionView(self.bot, self.guild_id, self.build_embed)
            embed = discord.Embed(title="📺 Supervisão", description="Adicione ou remova canais e categorias supervisionados.", color=0x5865F2)
            embed.add_field(name="Canais", value="Use o select abaixo para adicionar.", inline=False)
            embed.add_field(name="Categorias", value="Ou adicione categorias inteiras.", inline=False)
            await interaction.response.edit_message(embed=embed, view=view)
        elif value == "regras":
            view = AgentRulesView(self.bot, self.guild_id, self.build_embed)
            embed = discord.Embed(title="📚 Regras do Agente", description="Configure as regras (trigger → ação) do agente.", color=0x5865F2)
            await interaction.response.edit_message(embed=embed, view=view)
        elif value == "ia":
            view = AgentIAView(self.bot, self.guild_id, self.build_embed)
            embed = discord.Embed(title="🧠 Instruções e IA", description="Configure as instruções e ative as respostas da IA.", color=0x5865F2)
            await interaction.response.edit_message(embed=embed, view=view)
        elif value == "treino":
            view = AgentTrainView(self.bot, self.guild_id, self.build_embed)
            config = get_guild_config(self.guild_id)
            train_ch = config.get("agent_training_channel")
            embed = discord.Embed(title="📖 Canal de Treino", description=f"Canal atual: {f'<#{train_ch}>' if train_ch else 'Não definido'}", color=0x5865F2)
            await interaction.response.edit_message(embed=embed, view=view)
        elif value == "log":
            view = AgentLogView(self.bot, self.guild_id, self.build_embed)
            await interaction.response.edit_message(embed=view._log_embed(), view=view)
        elif value == "status":
            view = AgentStatusView(self.bot, self.guild_id, self.build_embed)
            config = get_guild_config(self.guild_id)
            embed = discord.Embed(title="⚡ Status do Agente", description=f"Agente: **{'Ligado' if config.get('agent_enabled') else 'Desligado'}**", color=0x5865F2)
            await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentDoubtView(discord.ui.View):
    """Categoria Dúvidas — canais monitorados, detecção e IA."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func
        self._add_remove_select()

    def _add_remove_select(self):
        """Adiciona select de remover canal com opções dinâmicas."""
        config = get_guild_config(self.guild_id)
        channels = config.get("agent_channels", [])
        guild = self.bot.get_guild(int(self.guild_id)) if self.guild_id else None
        opts = []
        for c in channels[:25]:
            ch = guild.get_channel(int(c)) if guild and c.isdigit() else None
            label = f"#{ch.name}" if ch else f"Canal {c}"
            opts.append(discord.SelectOption(label=label[:100], value=c, description="Remover"))
        if not opts:
            opts = [discord.SelectOption(label="(Nenhum canal)", value="_", description="Adicione canais primeiro")]
        select = discord.ui.Select(
            placeholder="➖ Remover canal",
            options=opts,
            custom_id="doubt_remove_ch",
            row=2,
        )
        async def callback(interaction: discord.Interaction):
            val = select.values[0] if select.values else ""
            if val == "_":
                return await interaction.response.send_message("Nenhum canal para remover.", ephemeral=True)
            config = get_guild_config(self.guild_id)
            chs = [c for c in config.get("agent_channels", []) if c != val]
            config["agent_channels"] = chs
            if not chs and not config.get("agent_categories"):
                config["agent_enabled"] = False
            save_guild_config(self.guild_id, config)
            view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
            await interaction.response.edit_message(embed=view._build_doubt_embed(), view=view)
            await interaction.followup.send("✅ Canal removido.", ephemeral=True)
        select.callback = callback
        self.add_item(select)

    def _build_doubt_embed(self) -> discord.Embed:
        config = get_guild_config(self.guild_id)
        chs = config.get("agent_channels", [])
        cats = config.get("agent_categories", [])
        use_ai = config.get("doubt_use_ai_validation", True)
        ai_on = config.get("agent_ai_enabled", False)
        instr = config.get("agent_instructions", "")
        has_api = bool(OPENAI_API_KEY or GROQ_API_KEY)
        embed = discord.Embed(
            title="❓ Dúvidas — Detecção e Respostas",
            description="Configure os canais que o agente monitora para identificar e responder dúvidas.",
            color=0x5865F2,
            timestamp=datetime.utcnow(),
        )
        ch_list = ", ".join(f"<#{c}>" for c in chs[:10]) if chs else "`Nenhum`"
        if len(chs) > 10:
            ch_list += f" (+{len(chs)-10})"
        cat_list = ", ".join(f"<#{c}>" for c in cats[:5]) if cats else "`Nenhum`"
        embed.add_field(name="📺 Canais monitorados", value=ch_list or "`Nenhum`", inline=False)
        embed.add_field(name="📁 Categorias", value=cat_list, inline=True)
        embed.add_field(name="🧠 Validação por IA", value="✅ Sim" if use_ai else "❌ Não (só padrões)", inline=True)
        embed.add_field(name="⚡ Respostas IA", value="✅ Ativo" if ai_on else "❌ Desativado", inline=True)
        embed.add_field(name="📝 Instruções", value="✅ Definidas" if instr else "❌ Não definidas", inline=True)
        embed.add_field(name="🔑 APIs", value=f"OpenAI {'✅' if OPENAI_API_KEY else '❌'} | Groq {'✅' if GROQ_API_KEY else '❌'}", inline=True)
        embed.set_footer(text="Configurações salvas automaticamente em guilds.json")
        return embed

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="doubt_add_channels",
        placeholder="➕ Adicionar canais para monitorar (selecione vários)",
        max_values=25,
        row=0,
    )
    async def add_channels(self, interaction: discord.Interaction, select: ChannelSelect):
        channels = select.values or []
        config = get_guild_config(self.guild_id)
        current = config.get("agent_channels", [])
        added = 0
        for ch in channels:
            cid = str(ch.id)
            if cid not in current and len(current) < MAX_AGENT_CHANNELS:
                current.append(cid)
                added += 1
        if added > 0:
            config["agent_channels"] = current
            config["agent_enabled"] = True
            save_guild_config(self.guild_id, config)
        view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._build_doubt_embed(), view=view)
        await interaction.followup.send(f"✅ {added} canal(is) adicionado(s). Total: {len(current)}", ephemeral=True)

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.category],
        custom_id="doubt_add_category",
        placeholder="📁 Adicionar categoria inteira",
        row=1,
    )
    async def add_category(self, interaction: discord.Interaction, select: ChannelSelect):
        cat = select.values[0] if select.values else None
        if not cat:
            return
        config = get_guild_config(self.guild_id)
        cats = config.get("agent_categories", [])
        cid = str(cat.id)
        if cid not in cats and len(cats) < MAX_AGENT_CATEGORIES:
            cats.append(cid)
            config["agent_categories"] = cats
            config["agent_enabled"] = True
            save_guild_config(self.guild_id, config)
        view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._build_doubt_embed(), view=view)
        await interaction.followup.send(f"✅ Categoria **{cat.name}** adicionada.", ephemeral=True)

    @discord.ui.button(label="Validação IA", emoji="🤖", style=discord.ButtonStyle.secondary, custom_id="doubt_toggle_ai_val", row=3)
    async def toggle_ai_val(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["doubt_use_ai_validation"] = not config.get("doubt_use_ai_validation", True)
        save_guild_config(self.guild_id, config)
        status = "✅ Ativada" if config["doubt_use_ai_validation"] else "❌ Desativada (mais permissivo)"
        view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._build_doubt_embed(), view=view)
        await interaction.followup.send(f"Validação por IA: {status}", ephemeral=True)

    @discord.ui.button(label="Respostas IA", emoji="💬", style=discord.ButtonStyle.secondary, custom_id="doubt_toggle_ai", row=3)
    async def toggle_ai(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["agent_ai_enabled"] = not config.get("agent_ai_enabled", False)
        save_guild_config(self.guild_id, config)
        status = "✅ Ativadas" if config["agent_ai_enabled"] else "❌ Desativadas"
        view = AgentDoubtView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._build_doubt_embed(), view=view)
        await interaction.followup.send(f"Respostas de IA: {status}", ephemeral=True)

    @discord.ui.button(label="Instruções", emoji="📝", style=discord.ButtonStyle.primary, custom_id="doubt_instructions", row=3)
    async def btn_instructions(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        modal = AgentInstructionsModal(self.guild_id, self.bot, lambda g: AgentDoubtView(self.bot, g, self.build_embed)._build_doubt_embed(), config.get("agent_instructions", ""))
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="doubt_back", row=4)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentSupervisionView(discord.ui.View):
    """Categoria Supervisão: canais e categorias."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="agent_add_channel",
        placeholder="➕ Adicionar canal",
        row=0,
    )
    async def add_channel(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        channels = config.get("agent_channels", [])
        cid = str(ch.id)
        if cid in channels:
            pass
        elif len(channels) >= MAX_AGENT_CHANNELS:
            return await interaction.response.send_message(
                f"⚠️ Limite de {MAX_AGENT_CHANNELS} canais atingido.", ephemeral=True
            )
        else:
            channels.append(cid)
            config["agent_channels"] = channels
            config["agent_enabled"] = True
            save_guild_config(self.guild_id, config)
        embed = discord.Embed(title="📺 Supervisão", description="Adicione ou remova canais e categorias supervisionados.", color=0x5865F2)
        await interaction.response.edit_message(
            embed=embed,
            view=AgentSupervisionView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Canal {ch.mention} adicionado.", ephemeral=True)

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.category],
        custom_id="agent_add_category",
        placeholder="➕ Adicionar categoria",
        row=1,
    )
    async def add_category(self, interaction: discord.Interaction, select: ChannelSelect):
        cat = select.values[0] if select.values else None
        if not cat:
            return
        config = get_guild_config(self.guild_id)
        categories = config.get("agent_categories", [])
        cid = str(cat.id)
        if cid in categories:
            pass
        elif len(categories) >= MAX_AGENT_CATEGORIES:
            return await interaction.response.send_message(
                f"⚠️ Limite de {MAX_AGENT_CATEGORIES} categorias atingido.", ephemeral=True
            )
        else:
            categories.append(cid)
            config["agent_categories"] = categories
            config["agent_enabled"] = True
            save_guild_config(self.guild_id, config)
        embed = discord.Embed(title="📺 Supervisão", description="Adicione ou remova canais e categorias supervisionados.", color=0x5865F2)
        await interaction.response.edit_message(
            embed=embed,
            view=AgentSupervisionView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Categoria **{cat.name}** adicionada.", ephemeral=True)

    @discord.ui.button(label="Remover canal/cat", emoji="➖", style=discord.ButtonStyle.danger, custom_id="agent_remove", row=2)
    async def remove_supervision(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        chs = config.get("agent_channels", [])
        cats = config.get("agent_categories", [])
        options = [discord.SelectOption(label=f"Canal #{c}", value=f"ch_{c}", description="Canal") for c in chs[:10]]
        options += [discord.SelectOption(label=f"Categoria {c}", value=f"cat_{c}", description="Categoria") for c in cats[:10]]
        if not options:
            return await interaction.response.send_message("Nada para remover.", ephemeral=True)
        view = AgentRemoveView(self.guild_id, self.bot, self.build_embed, options)
        await interaction.response.send_message("Selecione o que remover:", view=view, ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=2)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentRulesView(discord.ui.View):
    """Categoria Regras."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.button(label="Ensinar regra", emoji="📚", style=discord.ButtonStyle.primary, custom_id="agent_teach", row=0)
    async def teach(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = TeachAgentModal(self.guild_id, self.bot, self.build_embed)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Ver regras", emoji="📋", style=discord.ButtonStyle.secondary, custom_id="agent_list", row=0)
    async def list_teachings(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        teachings = config.get("agent_teachings", [])
        if not teachings:
            return await interaction.response.send_message("Nenhuma regra configurada.", ephemeral=True)
        lines = []
        for i, t in enumerate(teachings[:15], 1):
            lines.append(f"`{i}` **{t.get('trigger','?')[:30]}** → {t.get('action','?')}")
        embed = discord.Embed(title="📚 Regras do Agente", description="\n".join(lines), color=0x5865F2)
        await interaction.response.send_message(embed=embed, ephemeral=True)

    @discord.ui.button(label="Remover regra", emoji="🗑️", style=discord.ButtonStyle.danger, custom_id="agent_remove_rule", row=0)
    async def remove_rule(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        teachings = config.get("agent_teachings", [])
        if not teachings:
            return await interaction.response.send_message("Nenhuma regra para remover.", ephemeral=True)
        options = [
            discord.SelectOption(
                label=f"{i}. {t.get('trigger','?')[:70]}",
                value=str(i),
                description=f"Ação: {t.get('action','?')}",
            )
            for i, t in enumerate(teachings[:25])
        ]
        view = AgentRemoveRuleView(self.guild_id, self.bot, self.build_embed, options)
        await interaction.response.send_message("Selecione a regra para remover:", view=view, ephemeral=True)

    @discord.ui.button(label="Área de Treino", emoji="🎯", style=discord.ButtonStyle.secondary, custom_id="agent_train", row=0)
    async def train_area(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = AgentTrainModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=1)
    async def back_rules(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentIAView(discord.ui.View):
    """Categoria Instruções e IA."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.button(label="Instruções da IA", emoji="🧠", style=discord.ButtonStyle.primary, custom_id="agent_instructions", row=0)
    async def set_instructions(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        modal = AgentInstructionsModal(self.guild_id, self.bot, self.build_embed, config.get("agent_instructions", ""))
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="Modelo padrão", emoji="📝", style=discord.ButtonStyle.secondary, custom_id="agent_default_instructions", row=0)
    async def apply_default(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["agent_instructions"] = DEFAULT_INSTRUCTIONS
        save_guild_config(self.guild_id, config)
        embed = discord.Embed(title="🧠 Instruções e IA", description="Configure as instruções e ative as respostas da IA.", color=0x5865F2)
        await interaction.response.edit_message(embed=embed, view=AgentIAView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send("✅ Modelo padrão aplicado.", ephemeral=True)

    @discord.ui.button(label="IA Respostas", emoji="🤖", style=discord.ButtonStyle.secondary, custom_id="agent_ai_toggle", row=0)
    async def toggle_ai(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["agent_ai_enabled"] = not config.get("agent_ai_enabled", False)
        save_guild_config(self.guild_id, config)
        status = "✅ Ativada" if config["agent_ai_enabled"] else "❌ Desativada"
        embed = discord.Embed(title="🧠 Instruções e IA", description="Configure as instruções e ative as respostas da IA.", color=0x5865F2)
        await interaction.response.edit_message(embed=embed, view=AgentIAView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send(f"🤖 IA: {status}", ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=1)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentTrainView(discord.ui.View):
    """Categoria Canal de Treino."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="agent_train_ch_select",
        placeholder="Selecione o canal de treino",
        row=0,
    )
    async def select_train(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        config["agent_training_channel"] = str(ch.id)
        save_guild_config(self.guild_id, config)
        embed = discord.Embed(title="📖 Canal de Treino", description=f"Canal: {ch.mention}", color=0x5865F2)
        await interaction.response.edit_message(embed=embed, view=AgentTrainView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send("✅ Canal de treino definido.", ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=1)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentLogView(discord.ui.View):
    """Categoria Log e Tickets."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="agent_log_select",
        placeholder="Canal de log do agente",
        row=0,
    )
    async def select_log(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        config["agent_log_channel"] = str(ch.id)
        save_guild_config(self.guild_id, config)
        view = AgentLogView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._log_embed(), view=view)
        await interaction.followup.send("✅ Log definido.", ephemeral=True)

    def _log_embed(self) -> discord.Embed:
        config = get_guild_config(self.guild_id)
        embed = discord.Embed(title="📋 Log e Tickets", description="Log do agente, tradução e atuação em tickets.", color=0x5865F2)
        embed.add_field(name="Log", value=f"<#{config.get('agent_log_channel')}>" if config.get("agent_log_channel") else "Não definido", inline=True)
        embed.add_field(name="Canal Tickets (IA)", value=f"<#{config.get('agent_ticket_channel')}>" if config.get("agent_ticket_channel") else "Auto", inline=True)
        embed.add_field(name="Tradução", value="✅" if config.get("ticket_translation_enabled") else "❌", inline=True)
        embed.add_field(name="Em tickets", value="✅" if config.get("agent_in_tickets") else "❌", inline=True)
        return embed

    @discord.ui.select(
        cls=ChannelSelect,
        channel_types=[ChannelType.text],
        custom_id="agent_ticket_ch_select",
        placeholder="Canal de tickets (para IA mencionar)",
        row=1,
    )
    async def select_ticket_channel(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        config["agent_ticket_channel"] = str(ch.id)
        save_guild_config(self.guild_id, config)
        view = AgentLogView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=view._log_embed(), view=view)
        await interaction.followup.send(f"✅ Canal de tickets: {ch.mention}", ephemeral=True)

    @discord.ui.button(label="Tradução Tickets", emoji="🌐", style=discord.ButtonStyle.secondary, custom_id="agent_translation", row=2)
    async def toggle_translation(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["ticket_translation_enabled"] = not config.get("ticket_translation_enabled", False)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(embed=self._log_embed(), view=AgentLogView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send(f"🌐 Tradução: {'✅' if config['ticket_translation_enabled'] else '❌'}", ephemeral=True)

    @discord.ui.button(label="Agente em Tickets", emoji="🎫", style=discord.ButtonStyle.secondary, custom_id="agent_tickets", row=2)
    async def toggle_tickets(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["agent_in_tickets"] = not config.get("agent_in_tickets", False)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(embed=self._log_embed(), view=AgentLogView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send(f"🎫 Em tickets: {'✅' if config['agent_in_tickets'] else '❌'}", ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=3)
    async def back_log(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentStatusView(discord.ui.View):
    """Categoria Status."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.button(label="Ligar/Desligar", emoji="⚡", style=discord.ButtonStyle.primary, custom_id="agent_toggle", row=0)
    async def toggle(self, interaction: discord.Interaction, button: discord.ui.Button):
        config = get_guild_config(self.guild_id)
        config["agent_enabled"] = not config.get("agent_enabled", False)
        save_guild_config(self.guild_id, config)
        status = "✅ Ligado" if config["agent_enabled"] else "❌ Desligado"
        embed = discord.Embed(title="⚡ Status", description=f"Agente: **{status}**", color=0x5865F2)
        await interaction.response.edit_message(embed=embed, view=AgentStatusView(self.bot, self.guild_id, self.build_embed))
        await interaction.followup.send(f"⚡ {status}", ephemeral=True)

    @discord.ui.button(label="Voltar", emoji="⬅️", style=discord.ButtonStyle.secondary, custom_id="agent_back", row=0)
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = self.build_embed(self.guild_id)
        view = AgentConfigView(self.bot, self.guild_id, self.build_embed)
        await interaction.response.edit_message(embed=embed, view=view)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class AgentInstructionsModal(Modal, title="Instruções do Agente IA"):
    """Modal para definir as instruções/comportamento da IA."""

    def __init__(self, guild_id: str, bot, build_embed_func, current: str):
        super().__init__(timeout=300)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func
        self.add_item(
            TextInput(
                label="Instruções para a IA",
                placeholder="Ex: Você é o agente. Identifique dúvidas. Use <#ID> <@ID> <@&ID>",
                default=current[:4000] if current else "",
                required=False,
                style=discord.TextStyle.paragraph,
                max_length=4000,
            )
        )

    async def on_submit(self, interaction: discord.Interaction):
        instructions = self.children[0].value.strip()
        config = get_guild_config(self.guild_id)
        config["agent_instructions"] = instructions
        save_guild_config(self.guild_id, config)
        preview = (instructions[:150] + "...") if len(instructions) > 150 else instructions
        await interaction.response.send_message(
            f"✅ Instruções salvas!\n\n**Preview:** {preview}" if preview else "✅ Instruções salvas!",
            ephemeral=True,
        )


class TeachAgentModal(Modal, title="Ensinar regra ao Agente"):
    """Modal para adicionar uma regra de ensino."""

    def __init__(self, guild_id: str, bot, build_embed_func):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func
        self.add_item(TextInput(label="Trigger (palavra ou regex)", placeholder="Ex: palavrão ou (spam){2,}", required=True, max_length=200))
        self.add_item(TextInput(label="Ação", placeholder="delete | warn | delete_warn | log | reply", required=True, max_length=20))
        self.add_item(TextInput(label="Mensagem de resposta (para warn/reply)", placeholder="Opcional", required=False, max_length=300))

    async def on_submit(self, interaction: discord.Interaction):
        trigger = self.children[0].value.strip()
        action = self.children[1].value.strip().lower()
        response = (self.children[2].value or "Mensagem violou as regras.").strip()

        if action not in ("delete", "warn", "delete_warn", "log", "reply"):
            return await interaction.response.send_message("Ação inválida. Use: delete, warn, delete_warn, log ou reply.", ephemeral=True)

        is_regex = False
        if trigger.startswith("(") or "{" in trigger or "[" in trigger:
            try:
                re.compile(trigger)
                is_regex = True
            except re.error:
                pass

        config = get_guild_config(self.guild_id)
        teachings = config.get("agent_teachings", [])
        if len(teachings) >= MAX_TEACHINGS:
            return await interaction.response.send_message(
                f"⚠️ Limite de {MAX_TEACHINGS} regras atingido. Remova uma para adicionar outra.",
                ephemeral=True,
            )
        teachings.append({"trigger": trigger, "action": action, "response": response, "regex": is_regex})
        config["agent_teachings"] = teachings[-MAX_TEACHINGS:]
        save_guild_config(self.guild_id, config)

        await interaction.response.send_message(f"✅ Regra adicionada: `{trigger[:50]}` → {action}", ephemeral=True)


class AgentTrainModal(Modal, title="Área de Treino"):
    """Simula o agente com uma mensagem de teste."""

    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.add_item(TextInput(label="Mensagem de teste", placeholder="Digite como se fosse uma mensagem no chat", required=True, max_length=500))

    async def on_submit(self, interaction: discord.Interaction):
        content = self.children[0].value
        config = get_guild_config(self.guild_id)
        teachings = config.get("agent_teachings", [])

        triggered = []
        for t in teachings:
            trigger = t.get("trigger", "")
            is_regex = t.get("regex", False)
            matched = False
            if is_regex:
                try:
                    matched = bool(re.search(trigger, content, re.IGNORECASE))
                except re.error:
                    matched = trigger.lower() in content.lower()
            else:
                matched = trigger.lower() in content.lower()
            if matched:
                triggered.append(f"`{trigger[:30]}` → **{t.get('action')}**")

        if not triggered:
            await interaction.response.send_message(f"✅ Nenhuma regra dispararia para:\n> {content[:200]}", ephemeral=True)
        else:
            await interaction.response.send_message(
                f"⚠️ **Regras que disparariam:**\n" + "\n".join(triggered) + f"\n\nMensagem: > {content[:150]}",
                ephemeral=True,
            )


class AgentLogSelectView(discord.ui.View):
    """Seleção do canal de log."""

    def __init__(self, guild_id: str, bot, build_embed_func):
        super().__init__(timeout=60)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True

    @discord.ui.select(cls=ChannelSelect, channel_types=[ChannelType.text], custom_id="agent_log_select", placeholder="Selecione o canal de log")
    async def select_log(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        config["agent_log_channel"] = str(ch.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(content=f"✅ Log do agente: {ch.mention}", view=None)


class AgentTrainChannelSelectView(discord.ui.View):
    """Seleção do canal de treino do agente."""

    def __init__(self, guild_id: str, bot, build_embed_func):
        super().__init__(timeout=60)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True

    @discord.ui.select(cls=ChannelSelect, channel_types=[ChannelType.text], custom_id="agent_train_ch_select", placeholder="Selecione o canal de treino")
    async def select_train_channel(self, interaction: discord.Interaction, select: ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        config = get_guild_config(self.guild_id)
        config["agent_training_channel"] = str(ch.id)
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(content=f"✅ Canal de treino: {ch.mention}. Envie mensagens lá para o agente aprender.", view=None)


class AgentRemoveView(discord.ui.View):
    """Remover canal/categoria da supervisão."""

    def __init__(self, guild_id: str, bot, build_embed_func, options: list):
        super().__init__(timeout=60)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func
        self.options = options
        self._add_select()

    def _add_select(self):
        select = discord.ui.Select(placeholder="Remover...", options=self.options[:25], custom_id="agent_remove_select")
        select.callback = self._on_select
        self.add_item(select)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True

    async def _on_select(self, interaction: discord.Interaction):
        if not interaction.data or not interaction.data.get("values"):
            return
        val = interaction.data["values"][0]
        config = get_guild_config(self.guild_id)
        if val.startswith("ch_"):
            cid = val.replace("ch_", "")
            config["agent_channels"] = [c for c in config.get("agent_channels", []) if c != cid]
        else:
            cid = val.replace("cat_", "")
            config["agent_categories"] = [c for c in config.get("agent_categories", []) if c != cid]
        save_guild_config(self.guild_id, config)
        await interaction.response.edit_message(content="✅ Removido.", view=None)


class AgentRemoveRuleView(discord.ui.View):
    """Remover uma regra de ensino."""

    def __init__(self, guild_id: str, bot, build_embed_func, options: list):
        super().__init__(timeout=60)
        self.guild_id = guild_id
        self.bot = bot
        self.build_embed = build_embed_func
        select = discord.ui.Select(placeholder="Selecione a regra para remover...", options=options[:25], custom_id="agent_remove_rule_select")
        select.callback = self._on_select
        self.add_item(select)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True

    async def _on_select(self, interaction: discord.Interaction):
        if not interaction.data or not interaction.data.get("values"):
            return
        idx = int(interaction.data["values"][0])
        config = get_guild_config(self.guild_id)
        teachings = config.get("agent_teachings", [])
        if 0 <= idx < len(teachings):
            removed = teachings.pop(idx)
            config["agent_teachings"] = teachings
            save_guild_config(self.guild_id, config)
            trigger = removed.get("trigger", "?")[:50]
            await interaction.response.edit_message(content=f"✅ Regra removida: `{trigger}`", view=None)
        else:
            await interaction.response.edit_message(content="❌ Regra não encontrada.", view=None)


async def setup(bot):
    await bot.add_cog(AgentCog(bot))
