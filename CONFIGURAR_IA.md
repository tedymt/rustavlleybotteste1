# Como configurar a IA do Agente

## 1. Chave da API (obrigatório)

O agente precisa de uma chave para a IA. Você pode usar:

### Opção A: Groq (GRÁTIS)
1. Acesse https://console.groq.com
2. Crie uma conta
3. Vá em **API Keys** → **Create API Key**
4. Copie a chave e adicione no arquivo `.env`:
   ```
   GROQ_API_KEY=gsk_sua_chave_aqui
   ```

### Opção B: OpenAI (pago)
1. Acesse https://platform.openai.com/api-keys
2. Crie uma chave
3. No `.env`:
   ```
   OPENAI_API_KEY=sk-sua_chave_aqui
   ```

## 2. Configurar no Discord

1. Use `!sup` e selecione **Agente**
2. Clique em **Modelo padrão** para aplicar instruções iniciais (Rust Valley)
3. Edite em **Instruções da IA** se quiser personalizar
4. Adicione canais ou categorias para supervisionar (select no topo)
5. Ative **IA Respostas**
6. Ative **Ligar/Desligar**

## 3. Fluxo

- Mensagens em canais supervisionados são analisadas
- Se a IA identificar dúvidas, ela responde automaticamente
- Se for conversa normal, não responde
- Use `<#ID_DO_CANAL>` nas instruções para mencionar canais (ex: canal de tickets)

## 4. Treinar a IA com os plugins do servidor

Foi gerado o arquivo **`data/agent_treino_plugins.md`** com base na pasta `plugins/` do projeto. Esse arquivo contém:

1. **Instruções da IA** — texto pronto para colar em *Instruções da IA* no painel do Agente (resumo de Kits, Raidable Bases, Skill Tree, teleporte, economia, etc.).
2. **Frases para o Canal de Treino** — mensagens que você pode enviar no canal de treino do agente para ele aprender a responder dúvidas sobre cada plugin.
3. **Lista dos plugins** analisados e o que cada um faz.

Passos sugeridos:
1. Abra `data/agent_treino_plugins.md`.
2. Copie a seção *Instruções da IA* e cole em **!sup → Agente → Instruções e IA → Instruções da IA** (substitua ou complemente o modelo padrão).
3. Defina o **Canal de Treino** em **!sup → Agente → Canal de Treino** e envie lá as frases da seção *Frases para o Canal de Treino* (uma por mensagem).
4. Ajuste o texto conforme os comandos e regras reais do seu servidor.
