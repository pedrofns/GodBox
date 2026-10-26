# Changelog

## 0.8.2-alpha — primeira versão pública (ALPHA) / first public release (ALPHA)

### PT

- Padrão agora é o Ollama local (http://localhost:11434/v1, qwen2.5:7b, sem chave); a chave de API (OpenAI-compatível) é opcional.
- Mensagens claras no chat quando o Ollama não está rodando, o modelo não foi baixado, falta a chave ou não há conexão (checagem rápida de 2 s, sem travar o jogo).
- Novo "debug_log" (padrão false): logs detalhados só quando ligado; avisos e erros sempre.
- Desempenho: reflexão em cache, nada é feito por frame com o chat fechado, varreduras do Avatar mais raras.
- Inclui as novidades da 0.8.2 (religião de Deus, profetas, templos, livros sagrados) — ainda não testadas a fundo no jogo.

### EN

- Defaults to local Ollama (http://localhost:11434/v1, qwen2.5:7b, no key); an OpenAI-compatible API key is optional.
- Clear in-chat messages when Ollama isn't running, the model isn't pulled, the key is missing or there's no connection (quick 2 s check, no freeze).
- New "debug_log" (default false): verbose logs only when on; warnings and errors always.
- Performance: cached reflection, no per-frame work while the chat is closed, fewer Avatar sweeps.
- Includes the 0.8.2 features (God's religion, prophets, temples, holy books) — not yet thoroughly tested in-game.
