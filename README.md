![GodBox](docs/cover.jpg)

# GodBox

Mod de WorldBox por Pedro Nogueira. Versão **0.8.2-alpha** (ALPHA).
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3827692434) · [Changelog](CHANGELOG.md) · Licença [MIT](LICENSE)

## Português

**⚠ ALPHA: espere bugs.** As falas são geradas por IA e podem sair estranhas ou erradas. Interface e respostas em português (pt-BR).

Seja Deus no WorldBox: converse por IA com qualquer criatura. Ela responde de acordo com quem é no jogo (espécie, idade, profissão, reino, cidade, família, traços, guerras), usa dados reais do mundo e lembra das conversas.

- **Voz de Deus** (voz do céu) ou **Avatar de Deus** (aba GodBox): o NPC vê Deus em pessoa.
- **Religião de Deus** com nome, nome de Deus, rituais do próprio jogo, símbolo e postura com outras fés.
- **Profetas** que pregam e convertem cidades; **templos** e **livros sagrados** (com confirmação).
- **Bênçãos e maldições** reais e ordens com prazo.
- **Reconhecimento pela fama:** com um corpo possuído, o NPC só sabe de você o que a sua fama real permite.
- **Worldfall (opcional):** "Conversa livre (IA)" no menu E, primeira pessoa e ações do Worldfall pelo chat. **Não é obrigatório.** Sem ele o GodBox funciona como voz de Deus (sem primeira pessoa e sem Avatar).

### Instalação

1. WorldBox com o **Modo Experimental** ligado e o **NeoModLoader (NML)**.
2. **Ollama** (grátis, roda no seu PC, **não precisa de chave de API** e não vem junto com o mod): baixe em [ollama.com/download](https://ollama.com/download). No Prompt de Comando, rode `ollama pull qwen2.5:7b` e espere o download (~4,7 GB). Deixe o Ollama aberto enquanto joga.
3. **Steam Workshop:** [inscreva-se na página do GodBox](https://steamcommunity.com/sharedfiles/filedetails/?id=3827692434) e abra o jogo. Tecla do chat: **F3** (ou o botão "Conversar com..." na janela da unidade).
4. **Instalação manual:** baixe o zip da release (`GodBox_0.8.2-alpha.zip`) e extraia para `WorldBox/Mods/GodBox`, de modo que `mod.json` fique em `Mods/GodBox/mod.json`. Clonar este repositório nessa pasta também serve. Mantenha **só uma cópia** do mod (apague cópias manuais em `Mods/` se usar a Workshop).

**Hardware recomendado:** placa de vídeo com 8 GB de VRAM (NVIDIA/AMD) e 16 GB de RAM. Sem placa boa o Ollama roda na CPU, mas cada resposta pode levar dezenas de segundos.

**Alternativa opcional (sem Ollama):** uma API paga compatível com OpenAI (OpenAI, xAI, OpenRouter…). **Só esse caminho usa chave, e cada jogador usa a SUA própria chave:** no `config.json` troque `base_url`, `model` e `api_key` e clique em "Recarregar config". O mod não traz chave nenhuma. Cada jogador usa o próprio Ollama ou a própria chave; nada é compartilhado entre jogadores.

## English

**⚠ ALPHA: expect bugs.** Replies are AI-generated and can be odd or wrong. UI and replies are in Portuguese (pt-BR).

Be God in WorldBox: AI chat with any creature. It answers as who it is in the game (species, age, job, kingdom, city, family, traits, wars), uses real world data and remembers conversations.

- **God's voice** (voice from the sky) or the **Avatar of God** (GodBox tab): the NPC sees God in person.
- **God's religion**: name, God's name, the game's own rites, symbol, and stance towards other faiths.
- **Prophets** who preach and convert cities; **temples** and **holy books** (with confirmation).
- Real **blessings and curses**, and orders with deadlines.
- **Renown-based recognition:** in a possessed body, NPCs only know what your real fame allows.
- **Worldfall (optional):** "Conversa livre (IA)" in the E menu, first person, and Worldfall actions from the chat. **Not required.** Without it, GodBox works as God's voice (no first person, no Avatar).

### Installation

1. WorldBox with **Experimental Mode** on and **NeoModLoader (NML)**.
2. **Ollama** (free, runs on your PC, **no API key needed**, not bundled with the mod): download it from [ollama.com/download](https://ollama.com/download). In Command Prompt, run `ollama pull qwen2.5:7b` and wait for the download (~4.7 GB). Keep Ollama running while you play.
3. **Steam Workshop:** [subscribe on the GodBox page](https://steamcommunity.com/sharedfiles/filedetails/?id=3827692434) and start the game. Chat hotkey: **F3** (or the "Conversar com..." button in the unit window).
4. **Manual install:** download the release zip (`GodBox_0.8.2-alpha.zip`) and extract it to `WorldBox/Mods/GodBox`, so `mod.json` sits at `Mods/GodBox/mod.json`. Cloning this repository into that folder does the same thing. Keep **only one copy** of the mod (remove any manual copy under `Mods/` if you use the Workshop).

**Recommended hardware:** a GPU with 8 GB of VRAM (NVIDIA/AMD) and 16 GB of RAM. Without a capable GPU, Ollama runs on the CPU and each reply can take tens of seconds.

**Optional alternative (no Ollama):** a paid OpenAI-compatible API (OpenAI, xAI, OpenRouter…). **Only this route uses a key, and each player uses their OWN key:** set `base_url`, `model` and `api_key` in `config.json` and click "Recarregar config". No key ships with the mod. Each player uses their own Ollama or their own key; nothing is shared between players.

---

# GodBox 0.8.2-alpha

> **ALPHA.** Versão de testes: espere bugs. As falas são geradas por IA e podem ser estranhas, erradas ou repetitivas. A interface e as respostas são em português.

**GodBox** (antes "Worldfall AI Chat"): seja Deus dentro do mundo do WorldBox. Fale com qualquer criatura usando uma IA (LLM);
abençoe, amaldiçoe, declare guerras e pazes, crie a sua religião e espalhe-a por profetas, e caminhe entre os mortais como Avatar.
O personagem responde de acordo com quem ele é no jogo (raça, idade, profissão, reino, cidade, família, traços, guerras…),
lembra das conversas anteriores e, com o **Worldfall**, também usa as memórias e a opinião que o Worldfall guarda.

- **Integrado ao Worldfall (recomendado): sem ele, funciona só como voz de Deus, sem primeira pessoa nem Avatar.** O Worldfall é uma dependência *opcional*.
- Você usa o **seu próprio modelo de IA**: por padrão o **Ollama** local com `qwen2.5:7b` (grátis, nada sai do seu PC), ou a sua própria chave de uma API compatível com OpenAI. O mod não traz chave nenhuma.
- Versão 0.8.2-alpha, feita para WorldBox 0.51.2 (build 719) + NeoModLoader 1.2.1.1 + Worldfall 0.9.8.9.
- **Novo nome, mesmos dados:** só o nome visível mudou. O nome da pasta de instalação não importa (`Mods\WorldfallAIChat`, `Mods\GodBox` ou a pasta da Workshop), mas **só pode haver uma cópia** do mod instalada. A pasta de dados continua `WorldfallAIChat` (config.json, memórias, crônicas e rumores seguem valendo, sem migração). O id interno do mod também não mudou.

---

## 1. Instalação

1. **NeoModLoader (NML)**. Se o Worldfall já funciona no seu jogo, o NML já está instalado.
2. **Apague o mod antigo**: se existir a pasta `<WorldBox>\Mods\WorldfallAIDialogue` (a tentativa anterior),
   **apague-a**. Senão o NML vai continuar mostrando o erro de compilação `'MapBox' does not contain a definition for 'mapWidth'`.
3. **Steam Workshop:** inscreva-se no item do GodBox e pronto. **Manual:** copie esta pasta inteira para `<WorldBox>\Mods\GodBox\`
   (o `mod.json` precisa ficar direto em `Mods\GodBox\mod.json`, e não dentro de uma subpasta).
   Não deixe duas cópias ao mesmo tempo (por exemplo `Mods\WorldfallAIChat` **e** a da Workshop): apague a manual se usar a Workshop.
   - No Steam, `<WorldBox>` normalmente é `C:\Program Files (x86)\Steam\steamapps\common\worldbox`.
4. **Modelo de IA (padrão: Ollama, local e grátis):**
   1. Instale o Ollama: <https://ollama.com/download> (Windows: baixe e rode o instalador). O Ollama não vem junto com o mod e **não precisa de chave de API**.
   2. Num terminal (Prompt de Comando), rode `ollama pull qwen2.5:7b` (≈ 4,7 GB; uma placa de vídeo com 8 GB ajuda).
   3. Deixe o Ollama aberto (ele fica na bandeja do sistema). O endereço padrão já é `http://localhost:11434/v1`.
   Prefere uma API paga (OpenAI ou outra compatível)? Veja a seção 2.
5. Abra o jogo uma vez. O NML compila o mod sozinho. No menu de mods do NML deve aparecer **GodBox**. Tecla padrão do chat: **F3**.

## 2. Configuração e chave de API (opcional)

Na primeira vez que o jogo abre com o mod, ele cria este arquivo (**fora** da pasta do mod, de propósito):

```
C:\Users\<seu usuário>\AppData\LocalLow\mkarpenko\WorldBox\WorldfallAIChat\config.json
```

Dica: no Windows, cole `%USERPROFILE%\AppData\LocalLow\mkarpenko\WorldBox\WorldfallAIChat` na barra do Explorer.
Você também pode clicar em **"Abrir pasta"** na janela de conversa.

Com o Ollama e o `qwen2.5:7b` você não precisa mexer em nada. Para usar a OpenAI (ou outra API compatível, veja a seção 7):

1. Abra o `config.json` no Bloco de Notas.
2. Troque os campos (exemplo OpenAI):
   ```json
   "base_url": "https://api.openai.com/v1",
   "api_key": "sk-proj-COLE_SUA_CHAVE_AQUI",
   "model": "gpt-4o-mini",
   ```
   (modelos de raciocínio mais novos da OpenAI podem pedir `"max_tokens_field": "max_completion_tokens"`; o mod tenta sozinho de novo com o campo simples se o servidor recusar.)
3. Salve o arquivo. Depois, no jogo, clique em **"Recarregar config"** na janela de conversa, ou reinicie o jogo.

Para conseguir a chave: entre em <https://platform.openai.com/api-keys>, clique em **Create new secret key** e copie o valor (começa com `sk-`).
A API é paga à parte do ChatGPT Plus, então adicione crédito em <https://platform.openai.com/settings/organization/billing>.
Modelos pequenos (como `gpt-4o-mini`) custam uma fração de centavo por conversa curta.

> **Nunca** coloque a chave dentro da pasta do mod nem a compartilhe. A pasta do mod é o que vai para a Steam Workshop.
> O `config.json` com a chave e as memórias ficam só no seu PC, em `LocalLow`.

## 3. Como conversar

Há três jeitos de abrir a conversa:

| Jeito | Como |
|---|---|
| **Menu do Worldfall (tecla E)** | Possua uma criatura (G), chegue perto de alguém e aperte **E**. Na lista de falas do Worldfall aparece **"Conversa livre (IA)"**. Ela abre o chat com essa mesma criatura. |
| **Botão "Conversar"** | Abra a janela de inspeção de uma unidade (clique nela). Aparece o botão **"Conversar com <nome> (F3)"** embaixo, no centro da tela. |
| **Tecla F3** | Com uma unidade selecionada, ou possuindo uma criatura com alguém por perto, aperte **F3**. Aperte F3 ou Esc de novo para fechar. |

Na janela: digite e aperte **Enter** (ou "Enviar").
- **"Esquecer memória"** apaga o que essa criatura lembra de você.
- **"Recarregar config"** relê o `config.json`.
- **"Abrir pasta"** abre a pasta de dados.

Enquanto a janela está aberta, as teclas do jogo e do Worldfall ficam bloqueadas, para você poder digitar WASD, E, G etc.

Enquanto a conversa está aberta, o NPC **fica parado olhando para você**, como nas conversas do Worldfall. Se você está possuindo uma criatura, ela também fica parada. Ao fechar o chat (Esc, F3 ou "Fechar"), os dois voltam ao normal. Para desligar isso, use `"hold_units_while_chatting": false`.

**Quem está falando?**
- Se você está **possuindo** uma criatura no Worldfall, o NPC fala com essa criatura, "cara a cara".
- Se não está possuindo ninguém, você fala como uma voz misteriosa do céu (o deus do mundo). Isso pode ser mudado em `player_persona_when_not_possessing`.

### Fatos reais (v5)

A cada mensagem o mod lê **dados reais do jogo, na hora** e entrega ao modelo num bloco `FACTOS`: nome, idade, espécie, posição, trabalho, mortes, traços, clã, cultura, religião e idioma do NPC; a cidade dele (população, população máxima, guerreiros, comida, líder); o reino (população, nº de cidades, guerreiros, rei, capital, guerras, inimigos, aliança); o ano e quem está falando. O modelo é instruído a **usar só esses números e nomes** e a dizer que não sabe quando o dado não está lá. Para conferir, o bloco é gravado no log do NML a cada mensagem (procure `FACTOS for`). Desligue com `"facts_block_enabled": false` (bloco) ou `"log_facts_block": false` (só o log).

Dica Ollama: o prompt ficou maior. Se o NPC parecer ignorar os fatos, aumente o contexto do Ollama (variável de ambiente `OLLAMA_CONTEXT_LENGTH=8192` antes de iniciar o Ollama) ou baixe `history_turns_in_prompt`.

### Ações do Worldfall pelo chat (v6)

Com uma criatura **possuída** e o Worldfall instalado, o que você pede no chat pode **acontecer de verdade**, usando as mesmas opções do menu E do Worldfall (com as regras, preços, recusas e sorteios do próprio Worldfall):

1. Uma primeira chamada curta ao modelo (JSON, temperatura 0) decide se a sua frase pede uma ação, ou `nenhuma`. Perguntas não são ações.
2. O mod executa a opção do Worldfall por trás (uma conversa "sombra", invisível). Se o Worldfall recusar (sem ouro, já fez hoje, não gosta de você...), nada acontece.
3. A resposta do personagem é escrita sabendo o resultado real (`feito`, `falhou`, `recusado` + motivo). O bloco FACTOS já mostra o ouro/cidade novos.

Cada ação grava **uma linha** no log (`[WorldfallAIChat] ACTION ...`) e aparece um aviso no topo da tela.

Ações suportadas: dar ouro ou itens de presente, flertar, presente romântico, pedir para cortejar, pedir em casamento, terminar o romance, ter um filho, adotar órfão, mudar para a cidade, sair da cidade, alistar-se (soldado ou fundador de vila), sair do exército, desistir da vila, pregar a sua fé, seguir a fé do NPC, pedir trabalho, entregar missão, aprender uma habilidade, comprar (itens, armas e armaduras), vender, vender tudo, dormir na estalagem, contratar/renovar/dispensar guarda, comprar proteção, bênção, subornar, pagar multa, pagar ao rei para atacar outro reino, cobrar recompensa, conceder ou recusar petição, nomear conselheiro, comprar a liberdade de alguém, segredo de mover a terra, honrar a espada.

Ficam de fora (use o menu E): árvore genealógica (painel), conselho "Show me" (abre a interface do conselho), escolher terreno (precisa mirar), procurar pessoa (só informação), forja/retrabalho/venda de equipamento (menus de vários passos), conversa fiada (sem efeito), briga com rival (luta), e falar como deus (sem criatura).

Desligue com `"npc_actions_enabled": false` ou `"actions_two_pass": false`. `classifier_model` (vazio = o mesmo modelo) permite um modelo menor só para a decisão.



### Novidades da v0.8.2-alpha (primeira versão pública)
- **Padrão Ollama**: `http://localhost:11434/v1`, `qwen2.5:7b`, `api_key` vazia. Quem já tem `config.json` mantém os próprios valores.
- **Sem IA disponível, aviso claro no chat**: Ollama fechado (checagem de 2 s, sem esperar o `timeout_seconds`), modelo não baixado (`ollama pull qwen2.5:7b`), chave ausente/errada ou sem conexão. Depois de uma falha de conexão, novos pedidos nos 8 s seguintes falham na hora com a mesma mensagem (sem travar e sem repetir avisos). "Recarregar config" limpa isso.
- **`debug_log`** (padrão `false`): diagnóstico detalhado só quando ligado; avisos e erros sempre.
- **Desempenho**: reflexão em cache, nada por frame com o chat fechado, varredura do Avatar a cada 60 s.

### Novidades e correções da v0.8.2 (ainda não testada dentro do jogo)
- **Religião de Deus só nasce por você**: ela é criada somente pela janela "Religião de Deus" ou ao fazer o primeiro profeta, e é sempre uma religião NOVA do jogo (nunca a religião que o profeta ou a cidade já seguiam). As conversões feitas pelo próprio jogo na fundação (mesma espécie ao redor) contam como conversões do profeta.
- **Nome de Deus e primeiro contato**: os NPCs não sabem mais o nome de Deus por conta própria. Quem não segue a fé de Deus não sabe quem fala e pede que ele se revele; quem segue usa o nome do deus da religião de Deus. O nome padrão de uma religião do jogo nunca é adotado.
- **Voz x Avatar**: a voz do céu é uma voz na cabeça (o NPC não vê nada); com o Avatar o NPC vê Deus em pessoa.
- **Atitude diante de Deus**: os NPCs não pedem notícias a Deus; perguntam se o servem bem, pedem bênção, imploram, agradecem ou rezam. Animais entendem Deus e respondem com palavras simples de criatura (entre mortais e animais continua só som).
- **Janela de religião**: a linha da divindade mostra o nome de Deus na religião de Deus, o nome tradicional nas outras, ou "??".
- **Segurança das ações**: frases no futuro, condições e promessas não executam nada; "ótimo/obrigado" não repete a ação anterior; nova ação **retirar o dom de profeta** ("eu não te escolhi", "não és mais meu profeta"), com confirmação, remove os dons divinos.
- **Templos**: "construa um templo pra mim" usa a cidade do NPC; prazos ("você tem 3 anos", "ou será amaldiçoado") viram uma ordem conferida no fim do prazo; o NPC diz com honestidade se o templo está pronto ou em construção e sabe onde fica o templo da sua cidade.
- **Teletransporte**: "teleporte-se para o reino élfico/dos orcs/anão/humano" vai para a cidade mais próxima dessa espécie; "... e pregue lá" prega no destino.
- **Pregação**: corrigida a contagem; o profeta prega para quem ainda não segue a fé e, se a cidade inteira já segue, vai para a cidade mais próxima que não segue (prefere cidades com templo). Prazo de conversão conta conversões em qualquer lugar (inclusive as feitas pelo próprio jogo em conversas e livros, sem mudar as chances). Rótulo "Indo pregar" / "Pregando" na tarefa da unidade.
- **Livro sagrado** (nova ação): "escreva um livro sagrado" faz o profeta escrever um livro com a geração de livros do próprio jogo, com confirmação; se não houver cidade ou lugar para livros, diz por quê.
- **Pedidos do profeta**: profetas e fiéis devotos podem perguntar a Deus, no máximo uma vez por conversa, se ele deseja um templo (quando a cidade não tem) ou um livro sagrado; um "sim" segue o fluxo normal de confirmação.
- **Qualidade das respostas**: o NPC não fala como Deus, não se narra pelo nome, não repete o mesmo vocativo, não usa "unit" nem verbos aportuguesados do inglês, não inventa histórias, usa profetisa/escolhida no feminino e sabe se está amaldiçoado de verdade.

### Novidades e correções da v0.8.1 (ainda não testada dentro do jogo)
- **Só o Deus bom**: o Avatar Demônio e a forma demoníaca de Deus foram removidos (botão, criação, textos, patches de combate dos maléficos e regras "não maléficos odeiam / maléficos adoram"). Deus é sempre divino, com "voz do céu", e o Avatar de Deus é o único Avatar (corpo de Orbe de Luz, todas as magias, imortal e invencível). Os maléficos voltam à regra de antes da v0.8.0: desafiam Deus e podem atacar o Avatar. Se um mundo salvo na v0.8.0 tiver `_god_form.json` com "demonio", o arquivo é convertido para "divino" e isso vai para o log.
- **Avatar de Deus com corpo de Orbe de Luz**: a mutação do jogo `mutation_skin_light_orb` (antes `mutation_skin_energy`), só na subespécie própria do Avatar.
- **Ícones** (do próprio jogo, carregados em tempo de execução):
  - A aba GodBox usa o ícone do traço Cicatriz da Divindade (`scar_of_divinity`, `ui/Icons/actor_traits/iconDivineScar`).
  - O botão Avatar de Deus usa o ícone da Orbe de Luz.
  - O log mostra qual ícone foi encontrado e quais alternativas foram usadas.
- **Conversas mais honestas**:
  - "Já nos conhecemos" só vale se houve conversa de verdade antes (memória ou sessão anterior). Na primeira conversa, frases como "da última vez..." são refeitas ou cortadas.
  - Palavras comuns ("ore", "venha", "aqui"...) não são tratadas como nomes. Quando um nome não existe no mundo, o personagem diz que não conhece, sem "ouvi dizer que fica longe".
  - "Ore/reze para seu deus" gera só uma oração em personagem; nada acontece no mundo.
  - Perguntas e hipóteses ("e se eu te amaldiçoasse?", "você vai...?") nunca executam ações.
  - O personagem não se apresenta com outro nome ou cargo, não fala como você, não narra o que você faz e não começa com "Nome:". O prompt diz no início e no fim quem ele é e quem fala com ele.
  - Sem segundos, "tiles", "sistema" ou "ação" nas falas e nos avisos. Renomear e trocar de função aparecem em frases ("por ordem de Deus, X passou a se chamar Y"; "X deixou de ser agricultor e passou a trabalhar como construtor"), sem setas.
  - Rumores falam dos outros em terceira pessoa e com nome completo.
- **Líderes e reis reais**: quando o personagem erra quem manda, a correção usa frases naturais ("Faona é a líder de Nye Seon; o rei de Uaau é Oon"), só reinos de verdade (sem ids) e "eu sou"/"você é" quando for o caso. Gírias comuns ("manda-chuva", "grana"...) são explicadas ao modelo, e "quem manda?" recebe os governantes reais.
- **Nome de Deus**: "me chamo Pedro" também define o nome de Deus, e o personagem só reconhece o novo nome.
- **Fé**:
  - "O João está com minha palavra, escutem ele" é um ensinamento de Deus pelo profeta. Quem ouve pode se converter (chance real, com bônus, registrada no log).
  - "Quero que todos acreditem em mim" dito a quem não é profeta não faz nada; o personagem responde em personagem e pode sugerir o profeta.
  - A lista de profetas é um fato rígido; títulos de Deus vêm só da religião salva.
  - Prazo para o profeta ("converta alguém em 2 anos ou será amaldiçoado"): conta se houver pelo menos 1 conversão até o prazo; senão, maldição real no prazo, com log e rumor. Ameaças de morte não são executadas.
  - Pregar converte algumas pessoas próximas, com chance por postura, traços, templo e religião do reino (no log). O aviso de "a cidade adotou" só aparece se for verdade.
- **Templos**:
  - O profeta vai até o templo da cidade e prega lá (bônus de chance; bônus maior no templo de Deus).
  - Nova ação **erguer um templo** ("construa um templo em Uaau"), só para Deus/Avatar e com confirmação (sim/não). Confere a cidade, se já existe templo e os recursos. Usa a construção normal do jogo quando possível; senão ergue por milagre, e diz qual foi usada. Nunca vira troca de função nem fundação de vila.

### Novidades e correções da v0.8.0 (ainda não testada dentro do jogo)
> Na v0.8.1 o Avatar Demônio e a forma demoníaca de Deus (itens abaixo) foram removidos: Deus é sempre divino.
- **Dois Avatares na aba GodBox** (precisam do Worldfall; só um por vez):
  - **Avatar de Deus**: o anjo de energia de sempre.
  - **Avatar Demônio**: o `demon` do próprio jogo, numa subespécie própria. O ícone é o do demônio do jogo, carregado em tempo de execução (se falhar, usa o ícone do poder "demon" ou o `icon.png`). Ao sair corre o rumor "Um demônio caminhou entre nós e sumiu nas sombras."
- **Todas as magias para os dois Avatares**: teleport, summon_lightning, summon_tornado, cast_curse, cast_fire, cast_silence, cast_blood_rain, cast_grass_seeds, spawn_vegetation, spawn_skeleton, cast_shield e cast_cure. São as 12 magias reais do jogo, recolocadas a cada atualização de atributos, e a lista vai para o log. Para lançar, use o menu do Worldfall: Tab > Magia, escolha a magia e aperte X.
- **Imortal e invulnerável**: o traço real "immortal" é conferido de novo e registrado no log. O jogo não tem traço de invulnerável, então o estado real "invincible" é renovado a cada segundo.
- **Forma de Deus por mundo** (`_god_form.json` na pasta de memória do mundo):
  - Soltar um Avatar define a forma (divina ou demônio). Ela continua depois que o Avatar sai, e o padrão é divina.
  - Todas as conversas seguem essa forma, e a voz vira "voz do céu" ou "voz das sombras".
  - Ao mudar a forma, corre um rumor no mundo, e os NPCs lembram da forma anterior e podem comentar.
  - A forma atual aparece nos botões da aba (★) e no cabeçalho do chat.
- **Regra dura da forma demônio**: todo ser não maléfico ODEIA Deus e recusa as ordens dele (é ódio, não medo), e os santos o condenam. Seres maléficos o ADORAM, chamam-no de mestre ou senhor e obedecem. Isso vale no prompt, nas decisões de obediência, nas reações a bênçãos e nomes, e na religião: na forma demônio só os maléficos se convertem, profetas não maléficos se recusam a pregar e reis maléficos adotam a fé. Ninguém passa a atacar o Avatar além da hostilidade natural do jogo.
- **Nova janela da Religião de Deus**: campos "Nome da religião" e "Nome de Deus"; botão **"Editar rituais e costumes"**, que abre a janela de religião do PRÓPRIO jogo (`religion`) na aba de edição de traços (`ReligionTraitsEditor`) para a religião real de Deus (cria a religião antes, se ainda não existir; a nossa janela some e o mundo despausa enquanto ela está aberta; ao fechar, o mod relê os traços reais); botão **"Símbolo e cor"**, que abre a janela nativa `religion_customize`; **Postura com outras religiões** com explicação de uma linha (Tolerância: profetas só pregam, sem atrito; Conversão pacífica: profetas convertem, reis podem adotar a fé; Guerra santa: reis fiéis podem proibir outras fés e ficam mais dispostos a guerrear contra infiéis, guerra só com ordem de Deus confirmada); Salvar / Cancelar / Apagar. Saíram crença, mandamentos, pecados, rituais em texto livre, símbolo em texto e recompensas (ninguém ganha traço ao se converter). Profetas e fiéis descrevem a fé SÓ pelo nome, nome de Deus, rituais reais e postura, no tom da forma atual de Deus. `_god_religion.json` antigos continuam carregando (campos desconhecidos são ignorados).
- **Dons de profeta**: todo profeta ganha os traços reais `blessed` (abençoado), `chosen_one` (o escolhido), `super_health` (supersaúde) e `immortal` (imortal) via `addTrait`, com log; a fala narra a sensação, sem números. Profetas antigos recebem os dons ao serem tocados ou carregados.
- **Correções dos profetas**: orcs não contam mais como maléficos (o jogo dá `bloodlust` a todo orc); maléfico agora é só espécie/reino sombrio (mago maligno, necromante, demônio, mortos-vivos, caveiras, Gélidos) ou os traços `evil`/`psychopath`, com log por regra; uma visita converte cerca de 10–30% dos elegíveis, e a religião é conferida depois da conversão (a cultura com "raízes verdadeiras" do jogo continua bloqueando, e isso vai para o log); só o rei de verdade do reino decide adotar ou proibir (nunca um líder de cidade), com o nome certo do reino, rolagem e motivo no log; na conversão pacífica a proibição é rara; o texto usa sempre o nome atual de Deus e diz "tornou-se profeta da religião X".
- **Tecla padrão do chat agora é F3** (antes F8). Nem o WorldBox nem o Worldfall usam F3.
- **Correções da sessão de teste da 0.7.9:**
  - A obediência é decidida ANTES dos botões de escolha e da confirmação. Se o personagem vai recusar, ele recusa na hora, sem pedir confirmação. O sorteio vai para o log ("OBEDIENCE roll ... p(defy)=... roll=...").
  - Na forma divina, "desonesto" sozinho não faz ninguém desobedecer: só naturezas rebeldes (teimoso, ambicioso, cruel, psicopata, esquentado, paranoico, louco) dão chance de recusa.
  - O motivo da recusa é uma frase em português ("por ser ambiciosa e desonesta, não quer arriscar o reino nessa guerra") com os alvos reais. Ids de traços em inglês na resposta são trocados pelo nome em português do jogo.
  - O personagem não fala mais como Deus nem consigo mesmo. A confirmação é dirigida a Deus ("Confirmas, meu Deus?"). Se a resposta se chama pelo próprio nome ou narra a voz do céu, ela é refeita uma vez e, se persistir, essas frases são removidas.
  - Nomes de guerra aparecem sempre entre aspas, com o nome do jogo, sem tradução.
  - Ao aceitar uma missão, quem cumpre é o jogador: o personagem deseja sorte ou explica a recompensa. Frases em inglês como "shake on it" são trocadas.
- **Reconhecimento pela fama** (corpo possuído falando em primeira pessoa; não vale para Deus nem para o Avatar): cada NPC só sabe de você o que a fama real permite, mais o que ele próprio lembra.
  - Nível de fama 0–4 = o maior entre renome (`renown_tiers`, padrão 10/50/150/400), título (rei, líder de cidade, capitão, chefe de clã) e posto do Worldfall; +1 com traço notável (escolhido, abençoado, regicida, veterano, imortal) ou `notable_kills` mortes.
  - Fama 1: a mesma cidade sabe o nome. Fama 2: a cidade sabe nome e cargo, o reino sabe o nome. Fama 3: o reino sabe nome e cargo, o mundo sabe o nome. Fama 4: todos sabem, com feitos reais da crônica.
  - Parente, amante, melhor amigo e mesmo clã sempre conhecem você; seguidores da religião de Deus conhecem o profeta.
  - Postos do Worldfall (lidos por reflexão): soldado/guarda não dão fama; qualquer outro posto (capitão, marechal, intendente, chanceler, mago da corte, campeão, senhor, líder) dá ao menos o nível do reino; herdeiro e rei podem chegar ao mundo. O mestre dos espiões é secreto: só quem é do mesmo reino e já o conheceu sabe. Quem conhece você usa o título real em português ("o rei Uaau de Neen").
  - Desconhecido: o NPC não diz seu nome, pergunta quem você é e o que faz, e guarda o nome e a descrição que você der.
- **Checagens novas nas respostas** (uma regeneração, depois correção no código, tudo no log): escrita estrangeira (chinês etc.; corta no primeiro caractere estrangeiro), números de atributos que ninguém pediu, efeitos falsos de vida/vitalidade/felicidade, pessoas ou cargos inventados (ex.: ancião falso), apontar o ancião em perguntas de história antiga, vocativo com o nome errado, povo errado de um lugar ("os elfos de" uma cidade orc), acontecimentos inventados (festas, reuniões), nome de quem o NPC não conhece, e limite de tamanho (`max_reply_chars`).
- Perguntas sobre boatos ("sabe de algum boato?", "algo mais?") são respondidas só com os boatos reais, ou o NPC diz que não ouviu nada.
- Modelo local (Ollama/qwen): temperatura 0,6, top_p 0,9, repeat_penalty 1,1 (se o servidor aceitar), e a regra do idioma também como última linha do sistema.
- Missão do profeta: o tempo conta só com o jogo despausado; se ele empaca (180 s sem se aproximar) ou a caminhada passa do limite (mín. 600 s, cresce com a distância), refaz o caminho até 2 vezes e depois pula para a próxima cidade (até 3), sempre com o motivo no log.
- Deus pode mudar o próprio nome ("me chame agora de X", "meu nome agora é X", "pode me chamar de X") e perguntar "qual o meu nome?". Itens aparecem com o nome em português.

### Novidades e correções da v0.7.9
- **Rebrand para GodBox:** nome do mod, aba da barra de poderes, títulos das janelas e prefixo do log (`[GodBox]`). Id interno, pasta de instalação e pasta de dados não mudaram.
- **Aba "GodBox" na barra de poderes** (aba própria criada pelo NeoModLoader), com os botões **Avatar de Deus** e **Religião de Deus**. Ícones: o próprio ícone do jogo da mutação "ser energético" (`mutation_skin_energy`) para a aba e para o Avatar, e o ícone de religião do jogo (`ui/Icons/iconReligion`) para a Religião de Deus. Eles são carregados do jogo em tempo de execução, sem copiar nenhum PNG. Se falhar, o mod usa o próprio `icon.png` e registra no log.
- **Avatar de Deus** (precisa do Worldfall): solte o anjo no mapa e você o possui na hora, em primeira pessoa. É um anjo com pele de energia, numa subespécie própria. Deus fala pessoalmente: seres santos se ajoelham, céticos duvidam e malignos o desafiam. Ao sair (G, Esc ou V, ou morte/saída da posse), o Avatar some sem deixar corpo e corre o rumor "Deus caminhou entre nós e voltou aos céus". Configurações: `avatar_immortal` e `avatar_star`. Sem o Worldfall, o botão só mostra um aviso.
- **Religião de Deus:** o botão abre um formulário em português (o mundo fica pausado). Campos: nome da religião, nome de Deus, crença central, mandamentos (adicionar/remover), pecados, rituais, símbolo/cor, postura (tolerância, conversão pacífica ou guerra santa) e recompensa. Botões: Salvar, Cancelar e Apagar (com confirmação). O modelo é salvo por mundo em `memory/<mundo>/_god_religion.json`.
  - **Usa a religião real do jogo:** o nome do modelo vira o nome da religião de verdade, e as conversões são do próprio jogo (`tryToConvertToReligion`; cidade e reino via `setReligion`).
  - **Só no mod:** a doutrina (o jogo não tem mandamentos), as caminhadas dos profetas e as proibições dos reis.
  - **Profetas:** depois de "você será meu profeta" (ou "faça de X meu profeta"), o profeta anda até cerca de 4 cidades próximas enquanto o mundo roda e prega em cada uma. Converte conforme os traços (malignos rejeitam; abençoados e honestos aceitam mais fácil), a cidade adota a fé com 50% de fiéis, e surgem rumores e um aviso. Conforme a postura, o rei adota a fé ou a proíbe.
  - "espalhe minha religião em <cidade>" manda o profeta para essa cidade.
  - Só fiéis e profetas conhecem a doutrina, e o profeta é instruído a nunca inventar mandamentos. Guerra santa nunca começa sozinha: só com ordem explícita de Deus (`declare_war`, com confirmação).
- **Paz:** "faça as pazes com X" agora encontra o reino certo.
- **Ancião:** o personagem sabe quem é o ancião da cidade (sempre responde que sim e diz o nome) e não diz mais que matou o ancião sem ter matado.
- **Pronomes:** "marque onde ele está" e "me leve lá" usam a pessoa ou o lugar citado por último na conversa.
- **Palavras em inglês** soltas nas respostas em português são trocadas automaticamente (mostly, however, maybe…).
- **Promessas condicionais:** em "vá até X que eu tiro sua maldição" ou "se você chegar a X, eu te curo", o personagem anda até X e a recompensa só acontece na chegada.
- **Cidade por espécie:** "vá para uma cidade humana/élfica/anã/orc" e "a cidade mais próxima" funcionam.
- **Tirar maldição** mostra os atributos corretos logo depois (o status é removido e os atributos recalculados na hora).
- **Removido:** a tradução do menu do Worldfall (`translate_worldfall_ui`, a pasta `locale/` e o vigia de idioma). O Worldfall roda intocado, no idioma dele. A interface do GodBox (chat, aba, janela da religião, falas) continua em pt-BR.

### Novidades e correções da v0.7.8
- **Escolha de alvo pelo código:** "entre em guerra com os orcs" com 2 reinos de orcs não vira mais "sem efeito". O chat mostra botões (`Ne Ter`, `Zopodrid Of Glory`, `Ambos`, `Cancelar`) e também aceita respostas digitadas ("ne ter", "o primeiro", "os dois", "o mais perto", "cancelar"). Depois da escolha vêm os termos e a confirmação normais. "Ambos" declara guerra a todos com uma confirmação só. O mesmo mecanismo vale para fazer as pazes e para cidades com o mesmo nome.
- **Pergunta é pergunta:** enquanto falta escolher, o personagem tem que perguntar qual e não pode prometer nada. Se prometer ("Iniciarei os preparativos..."), a fala é refeita uma vez. Se ainda prometer, o código troca a fala por uma pergunta pronta.
- **Confirmação clara:** toda oferta pendente mostra os botões `Confirmar` / `Cancelar`. A fala tem que terminar perguntando sim/não com os termos; se não terminar, o código acrescenta "Confirma: declarar guerra a Ne Ter e atacar a cidade Vento? (sim/não)". Se a fala disser que já está começando, ela é refeita uma vez e, se insistir, é trocada pela pergunta.
- **Repetir a ordem confirma:** com uma oferta pendente, "ataque", "faça", "vai", "pode", "manda ver", "execute", "agora", ou a mesma ação com o mesmo alvo, contam como SIM e não criam uma oferta nova. Com uma escolha pendente, repetir o pedido mostra a escolha de novo.
- **Autoridade:** os FACTOS dizem quando quem fala é Deus ou o rei do personagem e que ele TEM autoridade para ordenar guerra. O personagem não pode mais dizer "não estou autorizado sem ordens dos deuses" falando com Deus; se disser, a fala é refeita.
- Mais promessas detectadas: iniciarei, começarei, prepararei, ordenarei, marcharemos, etc.
- **Testes do mago (0.7.8):** categorias de traço corretas; atalho em português para traços; "tire o item" destrói o item de verdade; o personagem não descreve efeito que não aconteceu (a fala é refeita uma vez e, se insistir, vira "*(Nada aconteceu: …)*"); o log mostra o classificador da voz de Deus com a confiança; nada de ids de espécie/subespécie crus (viram "linhagem de …"); "ir até", teletransporte e ataque a vila sem reino; bênção só com palavras explícitas; afirmações sobre atributos têm que bater com o antes/depois real.
- **Seres maléficos:** sabem que a voz é DEUS (o deus da religião deles), odeiam e desafiam de propósito; dizem "não tenho reino" quando é verdade; nunca "anjo caído" nem "só o rei ou os deuses podem…" (se disserem, a fala é refeita).
- **Poderes de Deus acontecem COM o personagem:** ele reage (dor, medo, gratidão), nunca "aceito/recuso essa ordem".
- **Matar:** trava dura no código (só mata com ordem clara de morte, também na confirmação); **order_kill** para mandar matar outra pessoa; ameaças ("faça ou morre") repetem a ordem recusada com o personagem amedrontado.
- **Nome de Deus:** "me chame de Glorio a partir de hoje" renomeia o deus da religião (ou guarda o nome na memória de quem não tem religião), com boato e aviso; seres maléficos zombam do nome.
- **Sem inglês:** frases em inglês na fala fazem a fala ser refeita uma vez; se sobrar, são cortadas.
- **Mortes:** o personagem fala dos próprios crimes na primeira pessoa ("você/eu matei"), e falar de si em terceira pessoa refaz a fala; perguntas como "deus matou alguém?" buscam em toda a crônica conhecida (causa, assassino, vítima, ano).
- **Identidade:** linha clara "VOCÊ é X; quem fala com você é Y"; aviso quando o nome do personagem é igual ao do reino; se ele disser que é o jogador, a fala é refeita.
- **Reis:** os FACTOS pegam o rei do jogo; rei morto vira "(morto)" nos boatos e um novo rei gera o boato "X é o novo rei de K".
- **Bênção em números:** o resultado (antes → depois) do último poder divino fica na memória do personagem; "o que minha bênção mudou?" cita esses números (o efeito temporário "inspirado" é separado). Porcentagens inventadas e "você se sente…" sobre si mesmo refazem a fala.
- O prefixo "Nome:" no começo da fala é removido.
- **Poderes de Deus viram sensações:** quando bênção, maldição, retirar maldição ou dar/tirar traço mudam atributos, estados ou traços, o RESULTADO traz uma linha SENSAÇÕES com cada mudança, a direção e uma ideia de frase em primeira pessoa (vida: "sinto um calor que fecha minhas feridas"; dano: "meus golpes pesam mais"; velocidade: "minhas pernas parecem leves como o vento"; expectativa de vida: "sinto que viverei mais anos"; inspirado: "uma inspiração me invade, mas sinto que vai passar"; maldição e perdas têm versões sombrias). O personagem fala de cada mudança real com as próprias palavras e só dá números se o jogador perguntar. Na bênção, os números guardados separam a parte permanente (traço) da temporária (inspirado, ~60 s). Mudanças inventadas, porcentagens inventadas e "você se sente…" sobre si mesmo refazem a fala.
- **Identidade:** a linha de identidade diz o cargo de quem fala (ex.: "Ofoona, o REI de K") e proíbe cumprimentar a si mesmo ("Olá, Norron" dito por Norron) e dizer que é o rei/tomou posse. Respostas com auto-cumprimento ou que tomam o nome/cargo de quem fala são refeitas uma vez.
- **Sem "jogador":** o personagem nunca diz "o jogador"/"player"; fala pelo nome ou "você". Refaz uma vez e, se insistir, troca a palavra por "você".
- **Marcar no mapa ≠ guiar:** "marque pra mim onde fica X", "mostre no mapa X", "aponte onde fica X" viram where_is (marcar): a câmera vai até a cidade, o chão brilha e aparece um aviso; ninguém anda. guide_to (o NPC caminha) só com "me leve / me guie / me acompanhe / vamos até". O guia com estrela não mudou.
- **Reis anteriores pela crônica:** perguntas como "quem reinou antes?", "quais foram os reis anteriores?" usam a crônica de mortes (papéis rei/rainha daquele reino) e os boatos de "novo rei". O personagem não promete "vou pesquisar / te aviso depois" (não existe tarefa para depois): diz agora o que sabe ou que não sabe.
- **Ancião da cidade:** o morador mais velho (calculado ao vivo) é o ancião/anciã e sabe disso; ele lembra a crônica inteira da cidade e do reino (fundação, governantes passados, mortes marcantes). Os outros só conhecem bem as mortes do próprio tempo de vida (no máximo ~30 anos), sabem quem é o ancião e, para história antiga, dizem que não sabem e indicam o ancião pelo nome (dá para pedir onde ele está ou ser levado até ele). Reis e líderes continuam conhecendo os governantes.

### Novidades e correções da v0.7.7
- **Maldição explicada:** o RESULTADO agora diz exatamente o que pegou e o que não pegou, e por quê. No jogo, o estado "amaldiçoado" (cursed) tem o traço **Cruel (evil)** como oposto, então magos malignos, necromantes e qualquer alma cruel são imunes à maldição. Isso vale também para a maldição do próprio jogo, e o mod não força. Nesses casos o mod só aplica o traço azarado e avisa que "a maldição não pegou". Se o alvo já estava amaldiçoado, a duração é só renovada e o RESULTADO diz isso. Se nada mudou, o resultado é "sem efeito".
- **Bênção:** igual ao jogo, o traço abençoado não pega em quem tem o traço Cruel (antes o mod removia o Cruel à força), e o RESULTADO explica o motivo.
- **Ameaça não é ordem:** frases como "obedeça ou morra", "você irá morrer" e "se não me obedecer, morrerá" não criam mais a oferta de matar; o NPC só reage à ameaça. Ordens diretas ("morra!", "eu te mato agora", "fulmine-o") continuam valendo, sempre com confirmação.
- **Seres maléficos (inimigos de Deus):** a classificação usa os dados do jogo:
  - reinos ou espécies marcados com as tags evil, undead ou demon: mago maligno, necromante, caveiras de fogo e saltitantes;
  - os reinos selvagens das trevas: mortos-vivos (esqueleto, fantasma, zumbis), demônio e Gélidos (cold_one);
  - unidades zumbi;
  - qualquer um com o traço Cruel (evil); para desligar isso, use `malefic_evil_trait: false`.

  Eles veem Deus (o "Deus dos vivos") como inimigo e recusam todas as ordens da voz do céu; a recusa é feita no código. Zombam de Deus ou o amaldiçoam, e podem implorar ou barganhar sob ameaça, mas continuam recusando. Também nunca aceitam ser profetas nem pregar. Os poderes de Deus continuam funcionando neles: matar, amaldiçoar (com as imunidades do jogo) e abençoar, que eles odeiam. Se tiverem religião, o deus dela é o seu "mestre sombrio", e "me chamem de X" não muda nada. Os outros personagens os conhecem como servos das trevas.
- **Seres sagrados (servos de Deus):** magos brancos (white_mage) e anjos (angle). Falam com reverência e chamam Deus pelo nome da sua fé ou de "Senhor dos Céus". Obedecem sempre, sem os traços de rebeldia, e só recusam o impossível. Recebem bênçãos com alegria e aceitam maldições como provas de fé. Para eles, seres maléficos são inimigos que podem atacar por Deus. Os outros os conhecem como servos dos céus.
- FACTOS: "Sua cultura" agora explica que é o nome do povo, não de uma cidade.

### Novidades e correções da v0.7.6
- **Fundar vila (corrigido):** o colono anda até a terra livre escolhida e o prazo agora depende do progresso (não é mais fixo). Ao chegar, se o lugar deixou de servir, o mod escolhe a terra livre válida mais próxima em vez de falhar. Se o colono empacar (`village_stuck_seconds`, padrão 45 s sem avançar) ou passar de `village_walk_seconds` (padrão 420 s), a vila é fundada na melhor terra livre perto dele, ou à distância no lugar escolhido, como o próprio jogo faz na expansão. A direção pedida ("ao norte", "a nordeste"...) é respeitada; se não houver terra livre naquela direção, vale a mais próxima. O log mostra o progresso a cada ~10 s, a chegada, a validação com o motivo e o resultado. Sucesso ou falha geram um aviso de 8 s, o boato "X fundou a vila Y" e uma notícia para quem mandou, para o colono e para o rei, que o NPC conta na próxima conversa.
- **FACTOS:** "Você se tornou rei/rainha de K no ano N (antes: P)", e o mesmo para líder de cidade.
- **Guerra e paz:** `declare_war` e `make_peace` usam a diplomacia do próprio jogo. O alvo pode ser o nome do reino, a espécie ("os orcs"; se houver vários reinos, o NPC pergunta qual), o nome do rei ou o de uma cidade. Só Deus (os traços de rebeldia ainda podem levar a recusa) ou o rei possuído daquele reino podem pedir. O mod mostra os termos e pede confirmação, e o resultado traz o nome real da guerra, com boato e aviso.
- **Atacar cidade (`attack_city`):** o exército marcha. Contra cidade estrangeira, declara guerra se ainda não houver. Contra cidade do próprio reino, só Deus pode, com confirmação dupla: a cidade vira reino independente (rebelião) e há guerra. Se o alvo for um reino, a ação vira `declare_war`. "Ataque o X" com uma pessoa continua sendo `attack`.
- **Nomes aproximados:** "Naola" encontra "Naola Raadi", sem acento e por parte do nome, em todas as buscas de ações.
- **Honestidade:** quando a ação falha ou é recusada, o NPC diz que não deu certo e não promete nada; se prometer, a resposta é refeita uma vez. Quando o NPC aceita uma oferta, a aceitação é decidida no código. Se o NPC recusar com palavras ("isso não vai acontecer"), a oferta é cancelada e nada acontece depois. Se a ação foi feita e a resposta negar, a resposta é refeita. O NPC não inventa pessoas com nome.
- **Crônica de mortes:** o mod registra a morte de unidades notáveis em `memory/<mundo>/_deaths.json`:
  - quem já conversou;
  - reis, líderes e capitães;
  - profetas;
  - amores, pais e filhos de quem conversou;
  - quem foi morto por alguém que conversou.

  Cada registro guarda nome, espécie, idade, cidade, reino, ano, causa (velhice, fome, doença, afogado, fogo, em combate, poder divino...), o assassino e se foi ação do mod. A morte vira boato na cidade. A busca de nomes informa que a pessoa morreu: com detalhes exatos se ela era de perto (ou para o rei), como boato se era de longe. Quando vários têm o mesmo nome, o NPC é avisado e o mais provável é escolhido. FACTOS ganha a linha "Mortes recentes de conhecidos". O assassino sabe o que fez e só mente se tiver o traço enganador ou maligno. Opção `death_chronicle`.
- **Nome do deus por religião:** cada religião chama Deus por um nome próprio, guardado em `memory/<mundo>/_gods.json`. O nome padrão é gerado pelo gerador de nomes do próprio jogo (cultura ou espécie); se não der, sai de um gerador de sílabas por espécie. Para trocar, a voz do céu diz a um fiel "me chamem de X" (pode ter epíteto: "me chamem de Ilvar, o Pai das Florestas"), "meu nome é X" ou "seu deus se chama X". O novo nome gera boato e aviso. Os fiéis passam a chamá-lo assim, e o rótulo do chat mostra "X (voz do céu)". Outras religiões só conhecem esse nome por boato e podem achá-lo um falso deus. O nome da religião (por exemplo "Pedro") continua separado do nome do deus.
- Novas opções: `village_walk_seconds`, `village_stuck_seconds`, `action_war`, `death_chronicle`.
- **Não testado no jogo:** nada foi testado no jogo desde a v0.7.0/0.7.1. Compilado contra a 0.51.2 publicada.

### Correções da v0.7.5
- Voz de Deus + "me leve até Fulano": Deus não tem corpo para ser guiado, então isso vira "onde está" (câmera vai até a pessoa + estrela) e o NPC responde com o resultado real. "onde está X" / "cadê X" / "me leve até X" agora são detectados direto no código, sem depender do classificador.
- BUSCA DE NOMES: sempre que sua mensagem cita uma pessoa, cidade ou reino ("conhece o Pedro?", "onde está X?", ou só o nome na frase), o mod procura no mundo (sem acento, tolera pequenos erros de digitação) e passa ao NPC: se existe, se está vivo, cidade, reino e, para reis ou gente perto, direção e distância exatas; para quem está longe, só "boato" (direção aproximada). Se não existir, ele é avisado de que ninguém com esse nome existe. Assim o NPC não nega alguém que existe. Opção `name_lookup` (padrão `true`). Pessoas mortas não aparecem na busca (o jogo não guarda a lista).
- FACTOS agora tem "Sua patente/posição" (rei, líder da cidade, capitão, chefe do clã, soldado, plebeu, criança), e o NPC é instruído a responder isso direto.
- Regra mais forte ao guiar: caminho descrito só com a direção/distância reais; nada de ruas, alamedas ou palácios inventados.

### Novidades da v0.7.4
- Ao pedir para um NPC te levar até alguém (guiar), a pessoa de destino ganha a ESTRELA de favorito do WorldBox (e o ping do Worldfall, se você estiver num corpo). A estrela fica enquanto ele guia e sai quando o guia chega ou desiste, mas só se foi o mod que a colocou (estrelas que você mesmo pôs nunca são tiradas).
- "Onde está fulano?" também põe a estrela (e essa fica). O NPC é avisado e pode dizer que marcou a pessoa.
- Nova opção `mark_target_star` (padrão `true`).
- Atacar alguém do PRÓPRIO reino do NPC: só é aceito se a ordem vier da voz divina (que continua com a chance de desobediência pelos traços) ou do REI desse reino (você possuindo o rei). Líderes de cidade, outras pessoas e inimigos recebem recusa ("só o rei ou os deuses podem mandar atacar alguém do nosso reino"). A checagem é feita no código, não só no prompt.

### Correções da v0.7.3
- FACTOS agora diz o que cada traço FAZ (ex.: "Abençoado: +50% dano, +50% vida, +50% velocidade, ..."), os atributos atuais (vida atual/máxima, dano, armadura, velocidade, felicidade...) e os estados ativos (ex.: amaldiçoado, com o tempo restante e os efeitos).
- Depois de abençoar/amaldiçoar/dar ou tirar traço/dar item, o RESULTADO mostra a mudança real (ex.: "vida máxima 100 -> 150").
- Traços que ainda vinham em inglês (King, Commoner, Soldier, Courageous...) são traduzidos pelo nome em inglês quando o jogo não tem texto pt.
- Nova regra no prompt: perguntas sobre a própria saúde, força, bênção ou maldição são respondidas com esses números; nada de efeitos inventados.

### Correções da v0.7.2
- FACTOS mais "mastigados" para o modelo pequeno: cada cidade vem marcada **[DO SEU REINO]** ou **[OUTRO REINO (nome, aliado/inimigo/neutro)]**; novas linhas "Cidades do seu reino: Oon (capital, sua cidade), ...", "Outros reinos (NÃO são o seu): Vava (Humano; cidade mais próxima Zek, leste; inimigo)" e "Reinos do mundo: Neen (o SEU), ...".
- Novas regras no prompt: copiar nomes letra por letra, nunca dizer que uma cidade do seu reino é de outro reino, nunca citar o próprio reino como vizinho, usar as direções exatamente como escritas.
- Traços e espécies em português mesmo com o WorldBox em inglês (lidos dos textos pt do próprio jogo; senão uma tabela nossa; senão o id).
- "População máxima" era na verdade a soma das vagas das casas (`housing_total`); agora aparece como "Vagas em casas" e, se houver, "Moradores sem casa".

### Correções da v0.7.1
- **Foco automático do campo de texto**: ao abrir o chat, depois de enviar e quando chega a resposta, já dá para digitar sem clicar. O foco fica no campo enquanto o chat estiver aberto (a não ser que você clique em outra coisa dentro da janela; volta no próximo envio/resposta). Se mesmo assim as teclas não chegarem ao campo, um modo reserva digita no chat (`chat_typing_fallback`, padrão `true`). O Player.log mostra linhas `focus diag` nos primeiros quadros.

### Novidades da v0.7.0

**Chat**
- O campo de texto já vem **focado** ao abrir o chat (não precisa clicar).
- **O mundo pausa** enquanto o chat está aberto e volta ao fechar (`pause_world_during_chat`). Os prazos das ações (seguir, pregar, chamar...) só contam com o mundo rodando.
- **Idioma do Worldfall:** o mod força `br` só na memória (sem mexer em arquivos do Worldfall). Se você trocar o idioma do Worldfall durante a sessão, a sua escolha é respeitada.
- **Vazamento de prompt corrigido:** a resposta é cortada se o modelo começar a escrever falas do jogador, etiquetas `[Nome, ano N]`, marcadores `<|im_...|>`, `??` soltos ou uma linha de **outro personagem** (`Raar: ...`).
- **Resumo da memória corrigido:** resumos ruins (vazios, em inglês, com instruções) são descartados; `summary_max_chars` limita o tamanho.

**Honestidade (regras do prompt)**
- O NPC não inventa ações, lugares, caminhos nem ofertas. Também **não promete nada para o futuro** ("na próxima semana", "amanhã eu começo..."): só o que está EM ANDAMENTO no bloco SITUAÇÃO ATUAL pode ser descrito como acontecendo.
- Lista **"O QUE VOCÊ PODE OFERECER"**: só o que o jogo realmente faz.
- O NPC **nunca fala nem age como outro personagem** e **nunca diz que alguém chegou** a menos que o jogo diga isso.
- **FACTOS** mais detalhado (cidade, reino, guerras, alianças) e **conhecimento do mundo** (cidades e reinos próximos, boatos): `world_knowledge`, `nearby_city_chunks`, `world_max_exact_cities`, `world_max_rumors`, `world_knowledge_max_chars`.

**Confirmações**
- Quando o NPC oferece algo ("quer que eu te leve?"), um "sim" do jogador **executa** a oferta pendente (`confirm_commitments`).
- Ações destrutivas (matar, atacar alguém do próprio reino, fundar vila, pregar no reino inteiro) precisam de **palavras-chave claras + confirmação** ("sim, mate") (`confirm_destructive`).

**Ações nativas (sem Worldfall)** — `native_actions_enabled`; cada uma tem `action_...`:
| Ação | Quem pode | O que acontece |
|---|---|---|
| Mudar nome | só **Deus** ou o **REI do mesmo reino** | Outros são bloqueados pelo código e o NPC recusa: *"só o rei ou os deuses podem mudar meu nome"*. |
| Siga-me / pare | criatura possuída | O NPC segue você por `follow_seconds`. |
| Me leve até X | criatura possuída | O NPC guia até a pessoa/cidade (`guide_seconds`). |
| Onde está X? | todos | Direção e distância reais + marcação do Worldfall/câmera. |
| Ataque X | Deus / quem tem posição maior | Ataca por `attack_seconds`. |
| Mudar trabalho / virar guerreiro | Deus / superiores | Troca o trabalho na cidade. |
| Fundar vila | Deus / rei ou líder do reino | Um colono vai fundar a vila (`village_walk_seconds`). |
| **Chamar / trazer alguém** (v7) | Deus / superiores | Veja abaixo. |
| **Levar recado** (v7) | Deus / qualquer um | Veja abaixo. |

Ordens de Deus podem ser **desobedecidas** por personalidades rebeldes (`god_defiance_base`); NPCs de posição mais alta recusam ordens de quem está abaixo; inimigos recusam (`enemy_opinion_threshold`). Cada ação grava uma linha `[WorldfallAIChat] ACTION ...` no log.

**Poderes divinos** (só como voz de Deus, `action_god_powers`): dar/tirar traço, abençoar, **amaldiçoar / tirar maldição**, dar arma ou armadura (materiais reais do jogo; não existe item de ouro), curar, matar (`action_kill`, `smite_lightning`).

**Maldição (v7)**: "eu te amaldiçoo!" agora funciona e **não pode ser desobedecida**. O WorldBox 0.51.2 **não tem traço "amaldiçoado"**, só o **estado** `cursed`. O mod aplica o estado `cursed` por `curse_seconds` (600 s de jogo), o traço **azarado** (`unlucky`) e uma queda forte de felicidade. "Eu retiro a sua maldição" encerra o estado, tira o `unlucky` e melhora o humor. "Eu te curo" também remove o estado.

**Religião** (`action_religion`): "você será meu profeta" cria (ou reaproveita) a religião de Deus, com o nome que você disser, e o NPC vira o fundador. Não existe traço/cargo de "profeta" no jogo: o mod guarda isso na memória do NPC. "Vá pregar" converte gente perto de verdade (`preach_seconds`, `preach_radius`, `preach_chance`); "converta a cidade/o reino" muda a religião da cidade/reino e de parte dos moradores (`preach_city_share`; o reino pede confirmação). Os números de convertidos são reais e ficam no log `ACTION`. Espécies sem memória avançada não podem ter religião.

**Chamar / trazer alguém (v7)**: "traga o Raar até mim" / "chame o rei aqui". O mod procura a pessoa no mundo (mesma busca do "onde está"). Se ela existir, **vai andando de verdade** até o NPC (ou até a sua criatura possuída). A resposta mostra a distância real e uma **estimativa** de tempo. Ela **só anda com o chat fechado** (o mundo fica pausado no chat). Quando chega, aparece um aviso; ao abrir o chat surge o botão **"Falar com Raar"**, que troca a conversa para ele. Inimigos e superiores recusam. Desiste depois de `summon_seconds`. O NPC é proibido de fingir a chegada ou de falar como o chamado.

**Recados e memória compartilhada (v7)**: "diga ao Raar que ele será meu profeta". O recado vai para a **memória do destinatário** ("Adda te trouxe um recado de Deus: ..."):
- na hora, se os dois moram na mesma cidade ou estão perto;
- senão, o mensageiro vai até lá e entrega ao chegar perto; depois de `message_max_seconds` o recado chega "por viajantes".
- Se o mensageiro ou o destinatário morrer, o recado se perde. Recados em trânsito ficam só na RAM (fechar o jogo antes da entrega perde o recado).

Quando você conversar com o Raar, ele **sabe do recado**.

**Boatos (v7)**: eventos marcantes viram boatos curtos em pt-BR para os moradores da **mesma cidade** (`rumors_enabled`, `rumors_per_city` = 8). Exemplos: Deus abençoou, amaldiçoou ou matou alguém; um novo profeta; troca de nome por ordem; novo líder ou rei. Ficam em `memory/<mundo>/_rumors.json`; os recados ficam no arquivo de cada unidade (`notes_per_unit` = 6).

**O que o jogo NÃO tem (e o mod não finge)**: traço "amaldiçoado", traço ou cargo de "profeta", itens de ouro. Ações do Worldfall que só existem como painel (árvore genealógica, conselho, forja) continuam só no menu E.

## 4. Idioma

Por padrão, os personagens respondem em **português do Brasil** (`"reply_language": "português do Brasil"`).
Nesse modo, as instruções para a IA também são escritas em português, o que ajuda muito os modelos locais pequenos.

- Para o personagem responder **no idioma em que você escreve**, use `"reply_language": "auto"` (ou deixe vazio `""`).
- Para fixar outro idioma: `"reply_language": "English"`, `"Español"` etc.
- **Já tinha o mod instalado?** O `config.json` antigo não muda sozinho. Abra o arquivo e troque a linha
  `"reply_language": ""` por `"reply_language": "português do Brasil"`. Depois clique em **"Recarregar config"**.

### Modelos locais (Ollama) que falam bem português

Para placas de vídeo com **8 GB** (ex.: RTX 3060 Ti):

| Modelo | Tamanho | Comentário |
|---|---|---|
| `qwen2.5:7b` | 4,7 GB | Bom equilíbrio entre velocidade e português. Boa escolha padrão. |
| `gemma3:4b` | 3,4 GB | Muito rápido, português natural. Às vezes é mais simples nas respostas. |
| `llama3.1:8b` | 4,9 GB | Bom em role-play, português correto. |
| `qwen3:8b` | 5,2 GB | Mais esperto, mas "pensa" antes de responder (mais lento). Se aparecer texto de raciocínio, prefira os outros. |
| `gemma3:12b` | 8,2 GB | O melhor português da lista, mas não cabe inteiro em 8 GB de VRAM: fica mais lento. |

Para trocar: `ollama pull gemma3:4b` e depois `"model": "gemma3:4b"` no `config.json`.

## 5. Memória

- Cada criatura tem a própria memória, separada por mundo. Os arquivos ficam em
  `...\LocalLow\mkarpenko\WorldBox\WorldfallAIChat\memory\<mundo>\<id da unidade>.json`.
- Quando a conversa fica longa, as falas antigas viram um **resumo** e só as mais recentes são mantidas.
  Isso é controlado por `summarize_after_messages`, `keep_recent_messages` e `max_summary_words`.
- **Salve o mundo depois da primeira conversa.** O mod grava uma identificação dentro do save do mundo.
  Sem salvar, ao recarregar o jogo ele não reconhece o mesmo mundo e as criaturas "esquecem".

## 6. Integração com o Worldfall

Tudo é feito por *reflection*. Se o Worldfall não estiver instalado, ou mudar numa atualização, o mod continua
carregando e o botão "Conversar" e a tecla F3 continuam funcionando.

- **Opção no menu E:** controlada por `worldfall_menu_option` (true/false) e `worldfall_menu_label` (o texto da opção).
  `worldfall_menu_position` aceita `"first"` (topo da lista) ou `"before_goodbye"` (logo antes de "Tchau").
- **Contexto:** a IA recebe da "Crônica" do Worldfall a família, as memórias e a opinião que o NPC tem de você.
  Desligue com `worldfall_integration: false`.
- **Contar conversas:** `worldfall_count_talks: true` faz cada conversa com IA contar como uma conversa no Worldfall,
  o que melhora a relação. Vem desligado.

## 7. Outros provedores (opcional)

Troque `base_url`, `model` e `api_key` no `config.json`:

| Provedor | base_url | model (exemplo) | Observação |
|---|---|---|---|
| OpenAI | `https://api.openai.com/v1` | ex.: `gpt-4o-mini` | Precisa de `api_key`. |
| xAI | `https://api.x.ai/v1` | o modelo de sua escolha | Coloque `"reasoning_effort": ""` se der erro. |
| OpenRouter | `https://openrouter.ai/api/v1` | ex.: `openai/gpt-4o-mini` | |
| Ollama (local, grátis, **padrão**) | `http://localhost:11434/v1` | `qwen2.5:7b` (recomendado; veja a seção 4) | `api_key` fica vazio; `"max_tokens_field": "max_tokens"` e `"reasoning_effort": ""` (já são o padrão). |
| LM Studio (local) | `http://localhost:1234/v1` | o modelo carregado | Mesmas observações do Ollama. |

## 8. Campos principais do config.json

O arquivo `config.example.json`, nesta pasta, mostra todos os campos com os valores padrão. Ele é só um exemplo: o mod lê o arquivo de `LocalLow`.

| Campo | Para que serve |
|---|---|
| `api_key` | A sua chave (`sk-...`). |
| `model` / `summary_model` | O modelo do chat e o modelo do resumo (vazio = o mesmo do chat). |
| `max_tokens` | O tamanho máximo da resposta. |
| `max_reply_sentences` | Quantas frases o personagem fala, no máximo. |
| `hotkey` | A tecla de atalho (padrão `F3`). Não use E, G, Q, WASD nem as teclas do Worldfall. |
| `show_talk_button` | Mostra ou esconde o botão "Conversar". |
| `extra_instructions` | Instruções extras para a IA, por exemplo "fale de forma medieval". |
| `debug_log` | v0.8.2-alpha: grava no log as linhas de diagnóstico (classificador, detalhes das AÇÕES, reconhecimento, FACTOS, pedidos…). Padrão `false`. Avisos e erros são sempre gravados. Ligue (`true`) para mandar um log de bug. |
| `debug_log_prompts` | Grava o prompt completo no log, para depurar (precisa de `debug_log`: `true`). |
| `reply_language` | Idioma das respostas. Padrão `"português do Brasil"`. `"auto"` ou vazio = o mesmo idioma em que você escreve. |
| `facts_block_enabled` | v5: envia dados reais do jogo (bloco FACTOS) e proíbe o modelo de inventar números. Padrão `true`. |
| `log_facts_block` | Grava o bloco FACTOS no log do NML a cada mensagem, para conferir com o jogo (só com `debug_log`: `true`). Padrão `true`. |
| `hold_units_while_chatting` | Mantém o NPC (e a sua criatura possuída) parado durante a conversa. Padrão `true`. |
| `npc_actions_enabled` | v6: o chat pode executar as ações do menu E do Worldfall. Padrão `true`. |
| `actions_two_pass` | v6: primeira chamada que detecta a ação + resposta com o resultado real. Padrão `true`. |
| `classifier_model` | v6: modelo só para detectar a ação (vazio = `model`). |
| `name_lookup` | v0.7.5: procura no mundo os nomes citados na sua mensagem e diz ao NPC se existem e onde estão. Padrão `true`. |
| `mark_target_star` | v0.7.4: guiar / "onde está" marcam a pessoa com a estrela de favorito (e o ping do Worldfall). Padrão `true`. |
| `chat_typing_fallback` | v0.7.1: se as teclas não chegarem ao campo de texto, o mod digita no chat mesmo assim. Padrão `true`. |
| `pause_world_during_chat` | v7: pausa o mundo com o chat aberto. Padrão `true`. |
| `native_actions_enabled` / `action_*` | v7: liga/desliga as ações nativas (`action_rename`, `action_follow`, `action_guide`, `action_where_is`, `action_attack`, `action_assign_job`, `action_create_village`, `action_summon`, `action_messages`, `action_god_powers`, `action_kill`, `action_religion`). |
| `confirm_commitments` / `confirm_destructive` | v7: confirmação de ofertas e de ações destrutivas. |
| `curse_seconds` | v7: duração da maldição (padrão 600 s de jogo). |
| `summon_seconds` / `summon_tiles_per_second` | v7: prazo do "chamar" e velocidade usada só na estimativa. |
| `message_max_seconds` / `notes_per_unit` | v7: prazo da entrega do recado e quantos recados cada unidade guarda. |
| `rumors_enabled` / `rumors_per_city` | v7: boatos por cidade. |
| `village_walk_seconds` / `village_stuck_seconds` | v0.7.6: limite da caminhada do colono (420 s) e tempo sem avançar antes do plano B (45 s). |
| `action_war` | v0.7.6: liga `declare_war`, `make_peace` e `attack_city`. Padrão `true`. |
| `malefic_evil_trait` | v0.7.7: quem tem o traço Cruel (evil) conta como ser maléfico (inimigo de Deus). Padrão `true`. |
| `death_chronicle` | v0.7.6: registra mortes de unidades notáveis (`_deaths.json`). Padrão `true`. |
| `gui_depth` | Camada da janela (padrão `-5000`). Precisa ser menor que `-1000` para ficar por cima da visão 3D do Worldfall. |

## 9. Problemas comuns

- **"Falta a chave da API"**: veja a seção 2.
- **Erro 401**: a chave está errada ou foi revogada.
- **"Sua conta da API está sem créditos/cota"** (`insufficient_quota`, HTTP 429): a conta da OpenAI está sem saldo. Adicione crédito em platform.openai.com > Billing. Não é um erro do mod.
- **"Muitas requisições seguidas"** (HTTP 429): espere alguns segundos e tente de novo.
- **Erro 404**: o nome do modelo ou o `base_url` está errado (com Ollama: rode `ollama pull qwen2.5:7b`).
- **"O Ollama não está rodando."**: instale (<https://ollama.com/download>) ou abra o Ollama (ícone na bandeja) e confira se o modelo foi baixado (`ollama list`; se faltar, `ollama pull qwen2.5:7b`).
- **"O modelo ... não foi baixado no Ollama"**: rode `ollama pull qwen2.5:7b` (ou o nome que está em `"model"`).
- **"O modelo não respondeu em N s"**: a primeira resposta do Ollama carrega o modelo e pode demorar; tente de novo ou aumente `timeout_seconds`.
- **A opção não aparece no menu E**: veja o log do jogo
  (`%USERPROFILE%\AppData\LocalLow\mkarpenko\WorldBox\Player.log`) e procure `[GodBox]`.
  Se o hook do Worldfall falhar, use o botão "Conversar" ou o F3.
- **A janela não aparece / o personagem parou**: aperte **Esc** ou **F3**, que sempre fecham a conversa e devolvem o controle.
  Se a janela não for desenhada em ~1,5 s, o mod fecha sozinho e devolve o controle. Ligue `"debug_log": true`, repita e mande o `Player.log`
  (linhas `[GodBox]`): com o debug ligado, cada passo é registrado (opção clicada, janela aberta/visível, conversa do Worldfall
  fechada, pedido enviado, resposta recebida, erros).
- **Erro de compilação de `WorldfallAIDialogue`**: é o mod antigo. Apague a pasta dele (seção 1).

## 10. Privacidade

O texto que você digita, o contexto da criatura e o resumo da memória são enviados ao provedor configurado (por padrão o Ollama no seu próprio PC).
Com Ollama ou LM Studio, nada sai do seu PC.

## Licença

MIT (veja `LICENSE`) para o código deste mod. Este mod não contém código, imagens nem arquivos (DLLs) do WorldBox ou do Worldfall; ele só chama o Worldfall e carrega ícones do jogo em tempo de execução.
O Worldfall é de s3cond2 (<https://worldfall3d.com>), "All Rights Reserved".

---

## English (short)

**GodBox 0.8.2-alpha** (ALPHA: expect bugs; replies are AI-generated; UI and replies are in Portuguese by default) (formerly "Worldfall AI Chat"): be God inside the world. Talk to any WorldBox unit through an AI model, bless, curse, wage war and make peace, found your religion and spread it through prophets, and walk among mortals as an Avatar. Each unit has a persistent, summarized memory.
**Worldfall recommended: without it, God-voice mode only, no first person or Avatar.** With Worldfall, the mod adds a **"Conversa livre (IA)"** option to Worldfall's E talk menu and feeds Worldfall's chronicle (kin, memories, opinion of you) into the prompt.
Only the display name changed: the install folder (`Mods\WorldfallAIChat`), the data folder (`WorldfallAIChat`) and the internal mod id are unchanged, so existing config and memories carry over.

**v0.8.2 (not yet tested in-game):**
- **God's religion is formed only by you**: only by the "Religião de Deus" window or by making the first prophet, and always as a NEW game religion (never the faith the prophet or the city already followed). The game's own founding conversions (same species nearby) count as the prophet's conversions.
- **God's name and first contact**: NPCs no longer know God's name on their own; non-followers ask God to reveal himself, followers use the god name of God's religion; a game religion's default god name is never adopted.
- **Voice vs Avatar**: the sky voice is a voice in the head (nothing is seen); the Avatar is God seen in person.
- **Attitude toward God**: NPCs never ask God for news; they ask whether they serve him well, ask for blessings, plead, thank or pray. Animals understand God and answer in simple creature words.
- **Religion window**: the deity row shows God's name for God's religion, the traditional name for others, otherwise "??".
- **Action safety**: future statements, conditions and promises run nothing; acknowledgements never re-run the last action; new confirmed action to **revoke a prophet**.
- **Temples**: default to the NPC's city, deadlines become an order checked at expiry, honest finished/under-construction state, NPCs know where their city's temple is.
- **Teleport** to species kingdoms (elven/orc/dwarf/human) and chained "... e pregue lá".
- **Preaching**: off-by-one fixed; targets non-followers, moves to the nearest city that does not follow yet (temple cities preferred); deadlines count conversions anywhere (base-game talk and book conversions are counted, odds unchanged); "Indo pregar" / "Pregando" task label.
- **Holy book** (new action): the prophet writes a book with the game's own book generation, after confirmation; honest failure when there is no city or book slot.
- **Prophet offers**: prophets and devout followers may ask God (once per conversation) whether he wants a temple or a holy book; "sim" goes through the normal confirmation.
- **Reply quality**: no speaking as God, no third-person self-narration, no repeated vocative, no "unit" or anglicised verbs, no invented lore, feminine forms (profetisa/escolhida), real cursed state.

**v0.8.1 (not yet tested in-game):**
- **Only the good God.** The Demon Avatar and God's demon form were removed: the button, spawn code, texts, the malefic combat patches and the "non-malefics hate / malefics adore" rules. God is always divine ("voz do céu"), and the Avatar of God is the only avatar (Light Orb body, all spells, immortal and invincible). Malefics are back to the pre-0.8.0 rule: they defy God and may attack the Avatar. A 0.8.0 world whose `_god_form.json` says "demonio" is migrated to "divino", and this is logged.
- The Avatar of God's body is the game's Light Orb skin (`mutation_skin_light_orb`).
- Icons are loaded at runtime from the game:
  - The GodBox tab uses the Scar of Divinity trait icon (`scar_of_divinity`).
  - The Avatar of God button uses the Light Orb icon.
  - Fallbacks are logged.

- More honest chat: "we've met" only after a real earlier conversation; common words aren't names and unknown names get a plain "I don't know them"; pray requests are prayer only; questions and hypotheticals never run actions; no identity/role swaps, speaking as you or "Name:" labels; no seconds, tiles or meta words; renames and job changes are prose, not arrows; rumours in third person with full names.
- Real rulers in natural sentences (civ kingdoms only, no ids, "I am"/"you are" when it applies); pt-BR slang glossary.
- "me chamo Pedro" sets God's name.
- Faith: God's word through a prophet can convert the listener (logged odds); "everyone must believe me" to a non-prophet does nothing; prophet list is a hard fact; deadline threats (≥1 conversion within N years, otherwise a real curse at the deadline; death threats aren't executed); preaching converts a few nearby people with logged odds.
- Temples: prophets preach at the city temple (bonus, bigger at God's temple). New confirmed **build a temple** action for God/Avatar: validates city, existing temple and resources; native construction first, miracle fallback, and it says which one was used.

**v0.8.0 (not yet tested in-game):** (the Demon Avatar and demon form below were removed in 0.8.1)
- Two avatars (one at a time, Worldfall needed). **Avatar of God** is the energy angel. **Demon Avatar** is the game's own `demon` unit on its own subspecies, with the game's demon icon loaded at runtime.
- Both avatars get all 12 real spells: teleport, summon_lightning, summon_tornado, cast_curse, cast_fire, cast_silence, cast_blood_rain, cast_grass_seeds, spawn_vegetation, spawn_skeleton, cast_shield and cast_cure. Cast them with Worldfall's Tab > Magic, then X.
- Both avatars have the real "immortal" trait and the "invincible" status, refreshed every second.
- God's form persists per world in `_god_form.json`: divine by default, or demon. It sets "voz do céu" or "voz das sombras", posts a rumour when it changes, NPCs remember the previous form, and it is shown in the tab buttons and the chat header.
- Demon form rule: every non-malefic being HATES God and refuses his orders. Malefic beings adore and obey him. This also applies to the faith: only malefic beings convert.
- New God's Religion window: religion name, god name, "Editar rituais e costumes" (opens the game's own `religion` window on its traits editor tab, creating the religion first if needed; our window hides and the world unpauses; traits are re-read on close), "Símbolo e cor" (native `religion_customize` window), stance with one-line explanations, Save/Cancel/Delete. Belief, commandments, sins, free-text rituals, symbol text and conversion rewards were removed. Prophets describe the faith only by name, god name, real rituals and stance. Old `_god_religion.json` files still load.
- Prophet gifts: real traits `blessed`, `chosen_one`, `super_health`, `immortal` (addTrait, logged; narrated without numbers; existing prophets get them when touched/loaded).
- Prophet fixes: orcs are no longer malefic (bloodlust is an orc birth trait); malefic = dark species/kingdom or `evil`/`psychopath` traits, logged per rule; a visit converts ~10-30% of eligible people and the religion is verified; only the real king decides adoption/ban (rolls and reasons logged; bans rare under peaceful conversion); texts use the current god name and say "became prophet of religion X".
- The default chat hotkey is now **F3**.
- Fixes:
  - obedience is decided before any confirmation, with the roll logged;
  - "deceitful" alone no longer causes defiance;
  - refusals give a Portuguese motive with the real targets;
  - trait ids are translated;
  - the NPC never addresses itself or speaks as God, and confirmation is asked of God;
  - war names stay quoted and untranslated;
  - on quest accept, the player does the quest;
  - "shake on it" and similar English phrases are scrubbed.
- Renown recognition (possessed body only): each NPC knows only what your real fame allows (renown tiers 10/50/150/400, titles, Worldfall ranks, notable traits/kills) plus its own memory. Family, lover, best friend and clan always know you. Plain soldiers/guards stay unknown; any other Worldfall rank is at least kingdom-known; spymaster is secret. Strangers are asked who they are, and the answer is remembered.
- New reply checks (one regeneration, then a code fallback, all logged): foreign scripts, unrequested stat numbers, fake vitality/happiness effects, invented people or roles, pointing to the elder, wrong-name vocatives, wrong species for a place, invented events, unknown speaker's name, `max_reply_chars`. Rumour questions use only real rumours.
- Local qwen: temperature 0.6, top_p 0.9, repeat_penalty 1.1 when accepted; the language rule is also the last system line.
- Prophet missions: unpaused time only, stall/long-walk detection, up to 2 re-paths, then skip to the next city (max 3), with reasons logged.

**v0.7.9:**
- "GodBox" power-bar tab with **Avatar of God** and **God's Religion** buttons. Icons are the game's own sprites, loaded at runtime: the `mutation_skin_energy` trait icon and `ui/Icons/iconReligion`. If loading fails, the mod's `icon.png` is used.
- **Avatar of God** (needs Worldfall): an energy-skin angel that you possess on drop. It vanishes quietly on exit, and NPCs react according to their nature.
- **God's Religion:** a pt-BR form to write your doctrine, saved per world. It names the real game religion, and conversions use the game's API. The doctrine, the prophets' missionary walks and the kings' bans are mod-side. Only followers and prophets know the doctrine. New orders: "faça de X meu profeta" and "espalhe minha religião em <cidade>". Holy war only happens on God's explicit, confirmed order.
- Other fixes: make_peace target, the city elder, pronouns ("ele", "lá"), stray English words, conditional promises, species and nearest cities, and lift_curse stats.
- **Removed:** the Worldfall UI translation, together with `translate_worldfall_ui` and `locale/`. Worldfall runs untouched in its own language.

**Requirements:**
- WorldBox 0.51.2 with Experimental Mode on, and NeoModLoader;
- an AI model: by default **Ollama** running locally with `qwen2.5:7b` (install from https://ollama.com/download, then `ollama pull qwen2.5:7b`; endpoint `http://localhost:11434/v1`), or your own OpenAI-compatible API key;
- Worldfall (optional).

**Install:**
1. Subscribe on the Steam Workshop, or put this folder in `<WorldBox>/Mods/GodBox/` (only one copy of the mod at a time).
2. Start the game once; it creates
   `%USERPROFILE%/AppData/LocalLow/mkarpenko/WorldBox/WorldfallAIChat/config.json`.
3. With Ollama nothing else is needed. For a paid API, set `"base_url"`, `"model"` and `"api_key"` there.
   The key is stored outside the mod folder and is never shipped.
4. Click "Recarregar config" in the chat window, or restart the game.
5. Bug report: set `"debug_log": true` (verbose diagnostics; off by default, warnings/errors are always logged) and send `Player.log`.

**Opening the chat:**
- the Worldfall E menu;
- the "Conversar" button while a unit's window is open;
- or **F3**.

Characters reply in Brazilian Portuguese by default. Set `"reply_language": "auto"` (or empty) to mirror the language you write in, or e.g. `"English"` to force another language. While the chat is open the NPC (and your possessed unit) stand still facing each other; turn this off with `"hold_units_while_chatting": false`. Good 8 GB-VRAM Ollama models: `qwen2.5:7b`, `gemma3:4b`, `llama3.1:8b`.
To use another provider, change `base_url` and `model`: xAI, OpenRouter, Ollama and LM Studio all work.

Licensed under MIT. The mod contains no WorldBox or Worldfall code.

v5 (true facts): every message, the mod reads live game data (the NPC, its city: population/max/warriors/food/leader, its kingdom: population/cities/warriors/ruler/capital/wars/enemies/alliance, the year, who is speaking) into a `FACTOS` block, and the model is told to use only those numbers and names and to say it doesn't know otherwise. The block is written to the NML log each turn (`FACTOS for ...`) when `debug_log` is on. Toggles: `facts_block_enabled`, `log_facts_block`.

**v6 — Worldfall actions from the chat:** with a possessed unit, a first JSON call (temperature 0) detects whether your message asks for one of Worldfall's E-menu actions (gift, trade, teach, enlist, move/leave city, romance, child, bribe, fine, guards, petitions...). The mod runs Worldfall's own option behind the scenes (Worldfall's prices, checks and rolls), then the character replies knowing the real result. One `ACTION` log line per action. Toggle with `npc_actions_enabled` / `actions_two_pass`. UI-only options (family tree, council panel, land plot, forge menus, fistfight) are not available from the chat.


**v0.7.8:**
- Worldfall's talk menu is kept in pt-BR. There is a postfix on Lang.Load (and on any other Load*/SetLanguage method) plus a 1-second watchdog that reloads br whenever Worldfall's language drifts, with a loop guard of 5 per minute. Every reload is checked for our extra lines, and every intercept is logged with its caller.
- A Portuguese choice made in Worldfall's settings menu no longer disables the override.
- Ambiguous targets are resolved by code. Example: "war with the orcs" when there are 2 orc kingdoms. The chat shows option buttons (each kingdom, Both, Cancel) and also takes typed answers (name, ordinal, "both", "nearest", "cancel"). The normal terms and confirmation follow the choice, and "Both" runs one order per kingdom. The same generic mechanism covers make_peace and same-name cities.
- While a choice is pending, the reply must be a question. A reply that promises is regenerated once, and if it still promises it is replaced by a code-built question.
- Every pending confirmation shows Confirm/Cancel buttons. The reply must end with a yes/no question that states the terms; if it doesn't, a code-built one is appended. A reply that claims the action is already starting is regenerated once, then replaced.
- While an offer is pending, repeating the order (an imperative like "ataque", or the same action and target) confirms it instead of creating a new offer. Repeating a request while a choice is pending shows the choice again.
- Authority: a FACTOS line plus a rule state that God, or the NPC's own king, can order war. Replies claiming they need orders from the gods or the king are regenerated.
- More promise verbs are detected (iniciarei, começarei, prepararei, ...).
- Mage-test fixes: trait categories, pt trait shortcut, take_item really destroys, no fake effects (regenerate once, then a code line), god-voice classifier confidence in the log, no raw species/subspecies ids, go_to/teleport/kingdomless raid, bless only on explicit words, stat claims must match the before/after diff.
- Malefic beings know the voice is God, hate and defy him knowingly, say "no kingdom" when true; "only the king or the gods can…" is regenerated.
- God powers happen TO the NPC: it reacts and never accepts/refuses. Kill has a hard code gate (also on confirm); new order_kill; threats re-run the refused order with a fearful tone.
- set_god_name: "me chame de Glorio a partir de hoje" renames the religion's god (or stores it in memory), with rumours.
- English sentences in a reply trigger one regeneration, then are stripped.
- Deaths: own kills in first person (third-person self-naming regenerates); filtered death queries over the whole known chronicle.
- Identity line "YOU are X; Y is talking to you"; claiming to be the speaker regenerates. Kings come from the game; dead kings are marked and new kings create a rumour.
- The last god power's before/after numbers are stored on the NPC and quoted on follow-ups; invented percentages and second-person self-description regenerate. A leading "Name:" prefix is stripped.
- God powers as feelings: bless/curse/lift_curse/add/remove trait put a SENSAÇÕES line in RESULT (each real stat/status/trait change, its direction, and a first-person feeling hint; gloomy versions for curses and losses). The NPC voices every real change in its own words and gives numbers only when asked. Stored bless numbers split permanent (trait) and temporary (inspired, ~60 s) parts. Invented changes/percentages and second-person self-description regenerate.
- Identity: the identity line states the speaker's role ("Ofoona, the KING of K") and forbids greeting yourself or claiming the throne; self-greetings and claiming the speaker's name/role regenerate once.
- No "player": replies never say "o jogador"/"player" (regenerate once, then replaced by "você").
- Mark ≠ guide: "marque/mostre no mapa/onde fica X" is where_is (mark): camera to the city, ground glow and a toast, nobody walks. guide_to only on "me leve / me guie / me acompanhe / vamos até". The star guide is unchanged.
- Past rulers come from the death chronicle (king/queen roles of that kingdom plus "new king" rumours); "I'll research / tell you later" promises regenerate and are stripped (there are no deferred tasks).
- City elder: the oldest living citizen (computed live) knows they are the elder and gets the whole city/kingdom chronicle (founding, past rulers, notable deaths). Others only know their own lifetime (max ~30 years) well, know who the elder is and point to them for old history. Kings and leaders keep their knowledge of rulers.

**v0.7.7:**
- **Curse:** RESULT now states exactly what applied and why not. The game's "cursed" status has the evil trait as its opposite, so evil mages, necromancers and evil-trait units are immune, even in vanilla. A repeated curse only refreshes the duration.
- **Bless:** the bless no longer strips the evil trait.
- **Threats:** threats or conditional sentences ("obey or die") no longer create a kill offer.
- **Malefic beings:** evil/undead/demon kingdom tags, the undead, demon and cold_one kingdoms, zombies, and the evil trait (`malefic_evil_trait`). They treat God as their enemy and refuse every god-voice order in code.
- **Holy beings:** white mages and angels revere God and obey without trait defiance.
- **FACTS:** the culture line is labeled as a culture.

**v0.7.6:**
- **Villages:** found-village orders now track the colonist's progress instead of a fixed timeout. On arrival, if the spot is no longer valid, the nearest valid free zone is used. A stuck colonist founds at the best valid zone nearby, or remotely like vanilla expansion. The requested direction is honoured.
- **Reporting:** the outcome is logged, toasted for 8 s, spread as a rumor and told to whoever gave the order.
- **King line:** FACTS says when and after whom the NPC became king or city leader.
- **War:** `declare_war`, `make_peace` and `attack_city` use vanilla diplomacy. Attacking an own city means rebellion and is God-only with a double confirmation. Only God or that kingdom's possessed king may give these orders.
- **Matching:** names are matched fuzzily (partial, accent-insensitive).
- **Honesty:**
  - a failed or refused action can't be promised;
  - a reply that refuses a pending offer cancels it;
  - a reply that denies an executed action is regenerated.
- **Death chronicle:** notable deaths go to `_deaths.json` with cause and killer, and feed rumors, name lookups and a FACTS line. The killer knows, and lies only if deceitful or evil. Toggle: `death_chronicle`.
- **God names:** each religion has its own name for God (`_gods.json`). The god voice can say "call me X". The chat label shows "X (voice from the sky)".
- **Untested:** none of this has been tested in-game yet.

**v0.7.5:** god-voice "take me to X" becomes where_is (camera + star); names of people/cities/kingdoms in the player's message are looked up in the live world (accent-insensitive, fuzzy) and given to the NPC (`name_lookup`); FACTS has an explicit rank line; stronger no-invented-landmarks rule.

**v0.7.4:** guide_to and where_is put WorldBox's favourite star (plus the Worldfall ping when embodied) on the target person; a star the mod added for a guide is removed when the guide ends, never one the player set. Option `mark_target_star`. Attacking someone from the NPC's own kingdom is only accepted from the god voice (trait-based defiance still applies) or from that kingdom's king (player possessing him); anyone else is refused, enforced in code.

**v0.7.3:** FACTS now include what each trait does (from its base_stats), current stats, and active statuses with time left; god actions report the concrete stat change (e.g. max health 100 -> 150); English-only trait names are mapped to Portuguese.

**v0.7.2:** FACTS now tag every city as YOUR KINGDOM / OTHER KINGDOM (name, ally/enemy/neutral), list your kingdom's cities and the other kingdoms explicitly, add rules to copy names and directions exactly, show traits/species in Portuguese from WorldBox's own pt texts, and label the housing number as housing slots (plus homeless count).

**v0.7.1:** the chat text box now really has keyboard focus on open, after sending and after each reply (focus is only set in input passes, never in Repaint, where IMGUI control ids differ; a typing fallback `chat_typing_fallback` covers the rest).

**v0.7.0:**
- **Chat:** the input box is auto-focused, and the **world pauses** while the chat is open (`pause_world_during_chat`).
- **Worldfall language:** the mod forces `br` only in memory and respects a language you pick mid-session.
- **Leak and summary fixes:** replies are cut at leaked transcript lines, including another character's `Name:` line, and bad summaries are dropped.
- **Honesty rules:**
  - no invented actions, places, routes or offers;
  - **no promises of future actions** ("next week");
  - a real "what you can offer" list;
  - the NPC never speaks as another character and never claims someone arrived unless the game says so.
- **Facts and world knowledge:** a richer FACTS block plus nearby cities, kingdoms and rumors.
- **Confirmations:** a "yes" carries out the NPC's pending offer. Destructive actions need clear keywords plus a confirmation.
- **Native actions** (work without Worldfall):
  - rename (**only God or the NPC's own king**; others are refused in character);
  - follow, guide, where-is, attack, change job, found a village;
  - **summon** and **relay a message**.
  - Higher-ranked units, enemies and (for God's orders) defiant personalities may refuse.
  - One `ACTION` log line per action.
- **God powers:** add or remove a trait, bless, give a weapon or armour, heal, kill, and **curse / lift curse**.
  - 0.51.2 has no "cursed" trait, only the `cursed` status. A curse applies that status for `curse_seconds`, plus the `unlucky` trait and a big happiness drop. It can't be defied.
- **Religion:** "you'll be my prophet" founds (or reuses) God's religion with the NPC as founder.
  - There is no prophet trait or role in the game; the mod keeps it in memory.
  - Preaching converts real units nearby, or a city/kingdom; counts are logged.
- **Summon:** "traga o Raar até mim".
  - The unit really walks to the speaker, but only while the chat is closed. You get the real distance and an ETA estimate.
  - On arrival you get a toast plus a "Falar com Raar" button that switches the chat.
  - Gives up after `summon_seconds`.
- **Messages:** "diga ao Raar que...".
  - Stored in the recipient's memory: immediately if same city or nearby; otherwise when the messenger gets close, or after `message_max_seconds` ("by travellers").
  - Messages in transit live in RAM only.
- **Rumors:** notable events (blessed, cursed, killed by God, new prophet, renamed, new leader or king) become short pt-BR rumors for units of the same city, capped by `rumors_per_city`.
- **Data location:** config and memory stay in `persistentDataPath/WorldfallAIChat`.
