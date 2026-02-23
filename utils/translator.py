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


def _ticket_ui_lang(lang: str) -> str:
    """Retorna código de idioma para TICKET_UI (en-GB->en, pt-PT->pt)."""
    m = {"en-GB": "en", "pt-PT": "pt"}
    return m.get(lang, lang)


# Strings do painel de ticket — cada chave tem traduções em pt, en, es, fr, de, it, ru
TICKET_UI = {
    "pt": {
        "ticket_title": "Atendimento Iniciado",
        "ticket_desc": "Olá! Nossa equipe foi notificada e irá atendê-lo em breve.",
        "protocol": "Protocolo",
        "server": "Servidor",
        "nick_in_game": "Nick in-game",
        "ask_reason": "Qual o motivo da abertura deste ticket? Descreva seu problema ou dúvida abaixo.",
        "confirm_created": "Ticket criado",
        "claim_channel": "{mention} — **{staff}** assumiu seu ticket e irá atendê-lo.",
        "claim_dm_title": "Ticket assumido",
        "claim_dm_desc": "Alguém da equipe assumiu seu ticket!\n**{staff}** irá atendê-lo em breve.",
        "open_ticket": "Abrir ticket",
        "close_confirm": "Este ticket será encerrado em {n} segundo(s).",
        "notify_dm_title": "Atualização no seu ticket",
        "notify_dm_desc": "A equipe de suporte te enviou uma atualização no seu ticket. Clique no botão abaixo para abrir o ticket.",
        "transfer_channel": "{mention} — Este ticket foi transferido para **{staff}**. Ele(a) irá atendê-lo.",
        "transfer_prompt": "Transferir ticket — Selecione um membro da equipe de suporte para assumir este ticket",
        "transfer_dm_title": "Ticket transferido",
        "transfer_dm_desc": "Seu ticket foi transferido para **{staff}**. Ele(a) irá atendê-lo em breve.",
        "closed_dm": "Atendimento finalizado em **{guild}**.\nProtocolo: `{code}` | Duração: {duration}\n\nResumo do ticket abaixo.",
        "panel_staff_title": "Painel Staff — Última mensagem do jogador",
        "panel_staff_waiting": "Aguardando mensagem do jogador...",
        "panel_ticket_lang": "Idioma do ticket",
        "only_author_support": "Apenas o autor do ticket e a equipe de suporte podem enviar mensagens aqui.",
        "err_category": "❌ A categoria de tickets configurada não existe mais. Peça a um administrador usar `!sup` → Ticket → reconfigurar a categoria.",
        "staff_only": "Apenas a equipe de suporte pode usar este botão.",
        "staff_only_claim": "Apenas a equipe de suporte pode assumir o ticket.",
        "staff_only_transfer": "Apenas a equipe de suporte pode transferir este ticket.",
        "modal_title": "Dados do Ticket",
        "nick_label": "Nick in-game",
        "nick_placeholder": "Seu nick no servidor",
        "steam_label": "Steam ID",
        "steam_placeholder": "Ex: 76561198012345678",
        "invalid_server": "Servidor inválido. Use um da lista (ex: EU1, BR1).",
        "invalid_steam_id": "Steam ID inválido. Use o Steam ID 64 (17 dígitos começando com 7656119). Ex: 76561198753318292",
        "transcript_desc": "Ticket fechado • Usuário {user} • Duração: {duration}",
        "user": "Usuário",
        "duration": "Duração",
        "transcript_footer": "Arquivo HTML em anexo para arquivo.",
        "log_title": "Ticket Fechado",
        "log_title_auto": "Ticket Fechado (auto: inatividade)",
        "log_footer": "Transcript enviado em DM (HTML) e no canal de transcripts",
        "summary_title": "Resumo do ticket #{code}",
        "summary_cont": "Resumo do ticket #{code} (cont.)",
        "summary_header": "Resumo do ticket #{code} @{name}",
        "maintenance_block": "⚠️ O bot está passando por uma manutenção e retornaremos em breve.",
        "maintenance_close_dm": "🔧 **Este ticket foi fechado automaticamente para manutenção.**\n\nSe o problema não foi resolvido, abra outro ticket quando retornarmos.\n\n**Protocolo:** `{code}`",
        "chat_closed_lock": "🔒 **Chat fechado pela staff para evitar brigas e discussões entre jogadores.**",
        "chat_unlock_btn": "Desbloquear chat",
    },
    "en": {
        "ticket_title": "Support Started",
        "ticket_desc": "Hello! Our team has been notified and will assist you shortly.",
        "protocol": "Protocol",
        "server": "Server",
        "nick_in_game": "Nick in-game",
        "ask_reason": "What is the reason for opening this ticket? Please describe your issue or question below.",
        "confirm_created": "Ticket created",
        "claim_channel": "{mention} — **{staff}** claimed your ticket and will assist you.",
        "claim_dm_title": "Ticket claimed",
        "claim_dm_desc": "Someone from the team claimed your ticket!\n**{staff}** will assist you shortly.",
        "open_ticket": "Open ticket",
        "close_confirm": "This ticket will be closed in {n} second(s).",
        "notify_dm_title": "Update on your ticket",
        "notify_dm_desc": "The support team sent you an update on your ticket. Click the button below to open the ticket.",
        "transfer_channel": "{mention} — This ticket was transferred to **{staff}**. They will assist you.",
        "transfer_prompt": "Transfer ticket — Select a support member to take over this ticket",
        "transfer_dm_title": "Ticket transferred",
        "transfer_dm_desc": "Your ticket was transferred to **{staff}**. They will assist you shortly.",
        "closed_dm": "Support finished on **{guild}**.\nProtocol: `{code}` | Duration: {duration}\n\nTicket summary below.",
        "panel_staff_title": "Staff Panel — Player's last message",
        "panel_staff_waiting": "Waiting for player message...",
        "panel_ticket_lang": "Ticket language",
        "only_author_support": "Only the ticket author and support team can send messages here.",
        "err_category": "❌ The ticket category no longer exists. Ask an admin to use `!sup` → Ticket → reconfigure the category.",
        "staff_only": "Only support staff can use this button.",
        "staff_only_claim": "Only support staff can claim this ticket.",
        "staff_only_transfer": "Only support staff can transfer this ticket.",
        "modal_title": "Ticket details",
        "nick_label": "Nick in-game",
        "nick_placeholder": "Your in-game nickname",
        "steam_label": "Steam ID",
        "steam_placeholder": "e.g. 76561198012345678",
        "invalid_server": "Invalid server. Use one from the list (e.g. EU1, BR1).",
        "invalid_steam_id": "Invalid Steam ID. Use Steam ID 64 (17 digits starting with 7656119). E.g: 76561198753318292",
        "transcript_desc": "Ticket closed • User {user} • Duration: {duration}",
        "user": "User",
        "duration": "Duration",
        "transcript_footer": "HTML file attached for archive.",
        "log_title": "Ticket Closed",
        "log_title_auto": "Ticket Closed (auto: inactivity)",
        "log_footer": "Transcript sent in DM (HTML) and to transcript channel",
        "summary_title": "Ticket summary #{code}",
        "summary_cont": "Ticket summary #{code} (cont.)",
        "summary_header": "Ticket summary #{code} @{name}",
        "maintenance_block": "⚠️ The bot is undergoing maintenance and we will return shortly.",
        "maintenance_close_dm": "🔧 **This ticket was closed automatically for maintenance.**\n\nIf the issue was not resolved, please open another ticket when we return.\n\n**Protocol:** `{code}`",
        "chat_closed_lock": "🔒 **Chat closed by staff to prevent fights and arguments between players.**",
        "chat_unlock_btn": "Unlock chat",
    },
    "es": {
        "ticket_title": "Atención Iniciada",
        "ticket_desc": "¡Hola! Nuestro equipo ha sido notificado y te atenderá en breve.",
        "protocol": "Protocolo",
        "server": "Servidor",
        "nick_in_game": "Nick in el juego",
        "ask_reason": "¿Cuál es el motivo de abrir este ticket? Describe tu problema o pregunta a continuación.",
        "confirm_created": "Ticket creado",
        "claim_channel": "{mention} — **{staff}** asumió tu ticket y te atenderá.",
        "claim_dm_title": "Ticket asumido",
        "claim_dm_desc": "¡Alguien del equipo asumió tu ticket!\n**{staff}** te atenderá en breve.",
        "open_ticket": "Abrir ticket",
        "close_confirm": "Este ticket se cerrará en {n} segundo(s).",
        "notify_dm_title": "Actualización en tu ticket",
        "notify_dm_desc": "El equipo de soporte te envió una actualización en tu ticket. Haz clic en el botón para abrir el ticket.",
        "transfer_channel": "{mention} — Este ticket fue transferido a **{staff}**. Te atenderá.",
        "transfer_prompt": "Transferir ticket — Selecciona un miembro del equipo de soporte para asumir este ticket",
        "transfer_dm_title": "Ticket transferido",
        "transfer_dm_desc": "Tu ticket fue transferido a **{staff}**. Te atenderá en breve.",
        "closed_dm": "Atención finalizada en **{guild}**.\nProtocolo: `{code}` | Duración: {duration}\n\nResumen del ticket a continuación.",
        "panel_staff_title": "Panel Staff — Última mensaje del jugador",
        "panel_staff_waiting": "Esperando mensaje del jugador...",
        "panel_ticket_lang": "Idioma del ticket",
        "only_author_support": "Solo el autor del ticket y el equipo de soporte pueden enviar mensajes aquí.",
        "err_category": "❌ La categoría de tickets ya no existe. Pide a un administrador usar `!sup` → Ticket → reconfigurar.",
        "staff_only": "Solo el equipo de soporte puede usar este botón.",
        "staff_only_claim": "Solo el equipo de soporte puede asumir este ticket.",
        "staff_only_transfer": "Solo el equipo de soporte puede transferir este ticket.",
        "modal_title": "Datos del ticket",
        "nick_label": "Nick in-game",
        "nick_placeholder": "Tu nick en el servidor",
        "steam_label": "Steam ID",
        "steam_placeholder": "Ej: 76561198012345678",
        "invalid_server": "Servidor inválido. Use uno de la lista.",
        "invalid_steam_id": "Steam ID inválido. Use Steam ID 64 (17 dígitos que comienzan con 7656119). Ej: 76561198753318292",
        "transcript_desc": "Ticket cerrado • Usuario {user} • Duración: {duration}",
        "user": "Usuario",
        "duration": "Duración",
        "transcript_footer": "Archivo HTML adjunto para archivo.",
        "log_title": "Ticket Cerrado",
        "log_title_auto": "Ticket Cerrado (auto: inactividad)",
        "log_footer": "Transcript enviado en DM (HTML) y al canal de transcripts",
        "summary_title": "Resumen del ticket #{code}",
        "summary_cont": "Resumen del ticket #{code} (cont.)",
        "summary_header": "Resumen del ticket #{code} @{name}",
        "maintenance_block": "⚠️ El bot está en mantenimiento y volveremos pronto.",
        "maintenance_close_dm": "🔧 **Este ticket fue cerrado automáticamente por mantenimiento.**\n\nSi el problema no se resolvió, abre otro ticket cuando volvamos.\n\n**Protocolo:** `{code}`",
    },
    "fr": {
        "ticket_title": "Assistance Démarrée",
        "ticket_desc": "Bonjour ! Notre équipe a été notifiée et vous assistera sous peu.",
        "protocol": "Protocole",
        "server": "Serveur",
        "nick_in_game": "Pseudonyme in-game",
        "ask_reason": "Quelle est la raison d'ouverture de ce ticket ? Décrivez votre problème ou question ci-dessous.",
        "confirm_created": "Ticket créé",
        "claim_channel": "{mention} — **{staff}** a pris en charge votre ticket et vous assistera.",
        "claim_dm_title": "Ticket pris en charge",
        "claim_dm_desc": "Quelqu'un de l'équipe a pris en charge votre ticket !\n**{staff}** vous assistera sous peu.",
        "open_ticket": "Ouvrir le ticket",
        "close_confirm": "Ce ticket sera fermé dans {n} seconde(s).",
        "notify_dm_title": "Mise à jour sur votre ticket",
        "notify_dm_desc": "L'équipe de support vous a envoyé une mise à jour. Cliquez sur le bouton pour ouvrir le ticket.",
        "transfer_channel": "{mention} — Ce ticket a été transféré à **{staff}**. Il/Elle vous assistera.",
        "transfer_prompt": "Transférer le ticket — Sélectionnez un membre du support pour prendre en charge ce ticket",
        "transfer_dm_title": "Ticket transféré",
        "transfer_dm_desc": "Votre ticket a été transféré à **{staff}**. Il/Elle vous assistera sous peu.",
        "closed_dm": "Assistance terminée sur **{guild}**.\nProtocole : `{code}` | Durée : {duration}\n\nRésumé du ticket ci-dessous.",
        "panel_staff_title": "Panel Staff — Dernier message du joueur",
        "panel_staff_waiting": "En attente du message du joueur...",
        "panel_ticket_lang": "Langue du ticket",
        "only_author_support": "Seuls l'auteur du ticket et l'équipe de support peuvent envoyer des messages ici.",
        "err_category": "❌ La catégorie de tickets n'existe plus. Demandez à un admin d'utiliser `!sup` → Ticket → reconfigurer.",
        "staff_only": "Seul l'équipe de support peut utiliser ce bouton.",
        "staff_only_claim": "Seul l'équipe de support peut prendre en charge ce ticket.",
        "staff_only_transfer": "Seul l'équipe de support peut transférer ce ticket.",
        "modal_title": "Détails du ticket",
        "nick_label": "Pseudonyme in-game",
        "nick_placeholder": "Votre pseudo sur le serveur",
        "steam_label": "Steam ID",
        "steam_placeholder": "Ex: 76561198012345678",
        "invalid_server": "Serveur invalide. Utilisez un de la liste.",
        "invalid_steam_id": "Steam ID invalide. Utilisez Steam ID 64 (17 chiffres commençant par 7656119). Ex: 76561198753318292",
        "transcript_desc": "Ticket fermé • Utilisateur {user} • Durée : {duration}",
        "user": "Utilisateur",
        "duration": "Durée",
        "transcript_footer": "Fichier HTML joint pour archive.",
        "log_title": "Ticket Fermé",
        "log_title_auto": "Ticket Fermé (auto : inactivité)",
        "log_footer": "Transcript envoyé en DM (HTML) et au canal transcripts",
        "summary_title": "Résumé du ticket #{code}",
        "summary_cont": "Résumé du ticket #{code} (suite)",
        "summary_header": "Résumé du ticket #{code} @{name}",
        "maintenance_block": "⚠️ Le bot est en maintenance, nous reviendrons bientôt.",
        "maintenance_close_dm": "🔧 **Ce ticket a été fermé automatiquement pour maintenance.**\n\nSi le problème n'a pas été résolu, ouvrez un autre ticket à notre retour.\n\n**Protocole :** `{code}`",
    },
    "de": {
        "ticket_title": "Support Gestartet",
        "ticket_desc": "Hallo! Unser Team wurde benachrichtigt und wird Ihnen bald helfen.",
        "protocol": "Protokoll",
        "server": "Server",
        "nick_in_game": "Nick im Spiel",
        "ask_reason": "Was ist der Grund für die Eröffnung dieses Tickets? Bitte beschreiben Sie Ihr Problem oder Ihre Frage unten.",
        "confirm_created": "Ticket erstellt",
        "claim_channel": "{mention} — **{staff}** hat Ihr Ticket übernommen und wird Ihnen helfen.",
        "claim_dm_title": "Ticket übernommen",
        "claim_dm_desc": "Jemand vom Team hat Ihr Ticket übernommen!\n**{staff}** wird Ihnen bald helfen.",
        "open_ticket": "Ticket öffnen",
        "close_confirm": "Dieses Ticket wird in {n} Sekunde(n) geschlossen.",
        "notify_dm_title": "Aktualisierung zu Ihrem Ticket",
        "notify_dm_desc": "Das Support-Team hat Ihnen eine Aktualisierung geschickt. Klicken Sie auf den Button, um das Ticket zu öffnen.",
        "transfer_channel": "{mention} — Dieses Ticket wurde an **{staff}** übertragen. Er/Sie wird Ihnen helfen.",
        "transfer_prompt": "Ticket übertragen — Wählen Sie ein Support-Mitglied aus, um dieses Ticket zu übernehmen",
        "transfer_dm_title": "Ticket übertragen",
        "transfer_dm_desc": "Ihr Ticket wurde an **{staff}** übertragen. Er/Sie wird Ihnen bald helfen.",
        "closed_dm": "Support beendet auf **{guild}**.\nProtokoll: `{code}` | Dauer: {duration}\n\nZusammenfassung des Tickets unten.",
        "panel_staff_title": "Staff Panel — Letzte Nachricht des Spielers",
        "panel_staff_waiting": "Warte auf Nachricht des Spielers...",
        "panel_ticket_lang": "Ticket-Sprache",
        "only_author_support": "Nur der Ticket-Autor und das Support-Team können hier Nachrichten senden.",
        "err_category": "❌ Die Ticket-Kategorie existiert nicht mehr. Bitten Sie einen Admin, `!sup` → Ticket → rekonfigurieren zu verwenden.",
        "staff_only": "Nur das Support-Team kann diesen Button verwenden.",
        "staff_only_claim": "Nur das Support-Team kann dieses Ticket übernehmen.",
        "staff_only_transfer": "Nur das Support-Team kann dieses Ticket übertragen.",
        "modal_title": "Ticket-Details",
        "nick_label": "Nick im Spiel",
        "nick_placeholder": "Ihr Nickname auf dem Server",
        "steam_label": "Steam ID",
        "steam_placeholder": "z.B. 76561198012345678",
        "invalid_server": "Ungültiger Server. Verwenden Sie einen aus der Liste.",
        "invalid_steam_id": "Ungültige Steam ID. Verwenden Sie Steam ID 64 (17 Ziffern, beginnend mit 7656119). Z.B: 76561198753318292",
        "transcript_desc": "Ticket geschlossen • Benutzer {user} • Dauer: {duration}",
        "user": "Benutzer",
        "duration": "Dauer",
        "transcript_footer": "HTML-Datei angehängt für Archiv.",
        "log_title": "Ticket Geschlossen",
        "log_title_auto": "Ticket Geschlossen (auto: Inaktivität)",
        "log_footer": "Transcript per DM (HTML) und an Transcript-Kanal gesendet",
        "summary_title": "Ticket-Zusammenfassung #{code}",
        "summary_cont": "Ticket-Zusammenfassung #{code} (Forts.)",
        "summary_header": "Ticket-Zusammenfassung #{code} @{name}",
        "maintenance_block": "⚠️ Der Bot wird gewartet, wir sind bald zurück.",
        "maintenance_close_dm": "🔧 **Dieses Ticket wurde automatisch für Wartung geschlossen.**\n\nFalls das Problem nicht gelöst wurde, öffnen Sie ein neues Ticket, wenn wir zurück sind.\n\n**Protokoll:** `{code}`",
    },
    "it": {
        "ticket_title": "Supporto Avviato",
        "ticket_desc": "Ciao! Il nostro team è stato avvisato e ti assisterà a breve.",
        "protocol": "Protocollo",
        "server": "Server",
        "nick_in_game": "Nick in-game",
        "ask_reason": "Qual è il motivo per aprire questo ticket? Descrivi il tuo problema o domanda qui sotto.",
        "confirm_created": "Ticket creato",
        "claim_channel": "{mention} — **{staff}** ha preso in carico il tuo ticket e ti assisterà.",
        "claim_dm_title": "Ticket preso in carico",
        "claim_dm_desc": "Qualcuno del team ha preso in carico il tuo ticket!\n**{staff}** ti assisterà a breve.",
        "open_ticket": "Apri ticket",
        "close_confirm": "Questo ticket verrà chiuso tra {n} secondo/i.",
        "notify_dm_title": "Aggiornamento sul tuo ticket",
        "notify_dm_desc": "Il team di supporto ti ha inviato un aggiornamento. Clicca sul pulsante per aprire il ticket.",
        "transfer_channel": "{mention} — Questo ticket è stato trasferito a **{staff}**. Ti assisterà.",
        "transfer_prompt": "Trasferire ticket — Seleziona un membro del team di supporto per assumere questo ticket",
        "transfer_dm_title": "Ticket trasferito",
        "transfer_dm_desc": "Il tuo ticket è stato trasferito a **{staff}**. Ti assisterà a breve.",
        "closed_dm": "Supporto concluso su **{guild}**.\nProtocollo: `{code}` | Durata: {duration}\n\nRiepilogo del ticket qui sotto.",
        "panel_staff_title": "Panel Staff — Ultimo messaggio del giocatore",
        "panel_staff_waiting": "In attesa del messaggio del giocatore...",
        "panel_ticket_lang": "Lingua del ticket",
        "only_author_support": "Solo l'autore del ticket e il team di supporto possono inviare messaggi qui.",
        "err_category": "❌ La categoria dei ticket non esiste più. Chiedi a un admin di usare `!sup` → Ticket → riconfigurare.",
        "staff_only": "Solo il team di supporto può usare questo pulsante.",
        "staff_only_claim": "Solo il team di supporto può prendere in carico questo ticket.",
        "staff_only_transfer": "Solo il team di supporto può trasferire questo ticket.",
        "modal_title": "Dettagli del ticket",
        "nick_label": "Nick in-game",
        "nick_placeholder": "Il tuo nickname sul server",
        "steam_label": "Steam ID",
        "steam_placeholder": "Es: 76561198012345678",
        "invalid_server": "Server non valido. Usa uno dalla lista.",
        "invalid_steam_id": "Steam ID non valido. Usa Steam ID 64 (17 cifre che iniziano con 7656119). Es: 76561198753318292",
        "transcript_desc": "Ticket chiuso • Utente {user} • Durata: {duration}",
        "user": "Utente",
        "duration": "Durata",
        "transcript_footer": "File HTML allegato per archivio.",
        "log_title": "Ticket Chiuso",
        "log_title_auto": "Ticket Chiuso (auto: inattività)",
        "log_footer": "Transcript inviato in DM (HTML) e al canale transcripts",
        "summary_title": "Riepilogo ticket #{code}",
        "summary_cont": "Riepilogo ticket #{code} (cont.)",
        "summary_header": "Riepilogo ticket #{code} @{name}",
        "maintenance_block": "⚠️ Il bot è in manutenzione, torneremo presto.",
        "maintenance_close_dm": "🔧 **Questo ticket è stato chiuso automaticamente per manutenzione.**\n\nSe il problema non è stato risolto, apri un altro ticket al nostro ritorno.\n\n**Protocollo:** `{code}`",
    },
    "ru": {
        "ticket_title": "Поддержка начата",
        "ticket_desc": "Здравствуйте! Наша команда уведомлена и скоро окажет вам помощь.",
        "protocol": "Протокол",
        "server": "Сервер",
        "nick_in_game": "Ник в игре",
        "ask_reason": "По какой причине вы открываете этот тикет? Опишите вашу проблему или вопрос ниже.",
        "confirm_created": "Тикет создан",
        "claim_channel": "{mention} — **{staff}** взял ваш тикет и окажет вам помощь.",
        "claim_dm_title": "Тикет взят",
        "claim_dm_desc": "Кто-то из команды взял ваш тикет!\n**{staff}** скоро окажет вам помощь.",
        "open_ticket": "Открыть тикет",
        "close_confirm": "Этот тикет будет закрыт через {n} сек.",
        "notify_dm_title": "Обновление по вашему тикету",
        "notify_dm_desc": "Команда поддержки отправила вам обновление. Нажмите кнопку, чтобы открыть тикет.",
        "transfer_channel": "{mention} — Этот тикет передан **{staff}**. Он/Она окажет вам помощь.",
        "transfer_prompt": "Передать тикет — Выберите члена команды поддержки для принятия тикета",
        "transfer_dm_title": "Тикет передан",
        "transfer_dm_desc": "Ваш тикет передан **{staff}**. Он/Она скоро окажет вам помощь.",
        "closed_dm": "Поддержка завершена на **{guild}**.\nПротокол: `{code}` | Длительность: {duration}\n\nКраткое содержание тикета ниже.",
        "panel_staff_title": "Панель Staff — Последнее сообщение игрока",
        "panel_staff_waiting": "Ожидание сообщения игрока...",
        "panel_ticket_lang": "Язык тикета",
        "only_author_support": "Только автор тикета и команда поддержки могут отправлять сообщения здесь.",
        "err_category": "❌ Категория тикетов больше не существует. Попросите администратора использовать `!sup` → Ticket → переконфигурировать.",
        "staff_only": "Только команда поддержки может использовать эту кнопку.",
        "staff_only_claim": "Только команда поддержки может взять этот тикет.",
        "staff_only_transfer": "Только команда поддержки может передать этот тикет.",
        "modal_title": "Данные тикета",
        "nick_label": "Ник в игре",
        "nick_placeholder": "Ваш ник на сервере",
        "steam_label": "Steam ID",
        "steam_placeholder": "Напр: 76561198012345678",
        "invalid_server": "Неверный сервер. Используйте один из списка.",
        "invalid_steam_id": "Недействительный Steam ID. Используйте Steam ID 64 (17 цифр, начинающихся с 7656119). Напр: 76561198753318292",
        "transcript_desc": "Тикет закрыт • Пользователь {user} • Длительность: {duration}",
        "user": "Пользователь",
        "duration": "Длительность",
        "transcript_footer": "HTML-файл приложен для архива.",
        "log_title": "Тикет Закрыт",
        "log_title_auto": "Тикет Закрыт (авто: неактивность)",
        "log_footer": "Транскрипт отправлен в ЛС (HTML) и в канал transcripts",
        "summary_title": "Краткое содержание тикета #{code}",
        "summary_cont": "Краткое содержание тикета #{code} (продолж.)",
        "summary_header": "Краткое содержание тикета #{code} @{name}",
        "maintenance_block": "⚠️ Бот находится на обслуживании, мы скоро вернёмся.",
        "maintenance_close_dm": "🔧 **Этот тикет был автоматически закрыт для обслуживания.**\n\nЕсли проблема не решена, откройте новый тикет по нашему возвращении.\n\n**Протокол:** `{code}`",
    },
}


def t(key: str, lang: str, **kwargs) -> str:
    """Retorna string do painel no idioma do usuário. Fallback: en."""
    l = _ticket_ui_lang(lang)
    d = TICKET_UI.get(l, TICKET_UI.get("en", {}))
    s = d.get(key, TICKET_UI.get("en", {}).get(key, key))
    return s.format(**kwargs) if kwargs else s


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
