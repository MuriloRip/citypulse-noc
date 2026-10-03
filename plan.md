# CityPulse — plano de implementação do MVP

## Direção

O CityPulse será entregue como um **NOC web responsivo, local-first e demonstrável**, separado do kernel FonsecaOS em `citypulse/`. Como o ambiente não possui o SDK .NET 8, o MVP usa Node.js sem dependências externas para manter a demo executável; o núcleo de polling está isolado em funções que podem ser portadas diretamente para `Parallel.ForEachAsync`, `Ping` e `HttpClient` no serviço C# definitivo.

## Design

- **Movimento:** civic-tech operacional com referências de terminais NOC e editorialização de dados públicos.
- **Princípios:** densidade informacional sem ruído, hierarquia por criticidade, estado sempre visível e ações curtas.
- **Cores:** navy quase preto para reduzir fadiga em plantões; ciano elétrico como assinatura de observabilidade; verde, âmbar e vermelho reservados para estado operacional.
- **Layout:** shell de três zonas: navegação estreita, canvas de operação e rail lateral de incidentes; evita dashboard genérico de cards centralizados.
- **Elementos de assinatura:** pulso ciano nos ativos online, trilha vertical de incidentes e mapa esquemático de topologia.
- **Interação/animação:** transições curtas, sem movimento ornamental; o pulso só comunica atividade e alertas entram com destaque de cor.
- **Tipografia:** Inter para leitura e IBM Plex Mono para IPs, timestamps e métricas.
- **Essência:** “a camada de consciência operacional da cidade” para equipes de infraestrutura e gestão pública. Personalidade: preciso, vigilante, público.
- **Voz:** títulos orientados a decisão; microcopy direta. Exemplos: “A cidade está operando.” e “A queda foi confirmada após 3 falhas consecutivas.”
- **Marca:** wordmark CityPulse com o “o” representado por um anel de pulso e um pequeno ponto de sinal.
- **Cor proprietária:** `#5DE4FF` (Pulse Cyan).

## Estrutura

- `server.js`: API HTTP, dados demonstrativos, motor de polling simulado, máquina soft/hard state, dependências e métricas.
- `public/index.html`: shell sem framework e marcação semântica do NOC.
- `public/styles.css`: sistema visual responsivo, estados, tabelas e mapa esquemático.
- `public/app.js`: consumo da API, atualização periódica, filtros, seleção de ativo e modal de cadastro.
- `manus-routes.json`: manifesto de rota exigido para preview e publicação.

## Escopo entregue

RF01 gestão visual de ativos com cadastro local; RF02 polling periódico simulado; RF03 confirmação após 3 falhas; RF04 dependência pai-filho e falha em cascata; RF05 MTTR e disponibilidade; RF06 painel NOC com alertas e tipo de problema. RNF02 é representado pelo processamento assíncrono do servidor; RNF04 pela fonte de dados em memória, pronta para troca por SQLite.
