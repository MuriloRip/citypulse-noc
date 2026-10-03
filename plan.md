# CityPulse — plano de implementação do MVP

## Direção

O CityPulse será entregue como um **NOC web responsivo, local-first e operacional**, separado do kernel FonsecaOS em `citypulse/`. O backend final usa ASP.NET Core .NET 8, EF Core SQLite, `Parallel.ForEachAsync`, `Ping` e `HttpClient`; não é um simulador de conectividade.

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

- `CityPulse.Api/Program.cs`: API minimal ASP.NET Core, CRUD, snapshot, health check e Swagger.
- `CityPulse.Api/Services/MonitoringService.cs`: probes ICMP/HTTP reais, concorrência limitada e máquina soft/hard state.
- `CityPulse.Api/Data/CityPulseDbContext.cs`: persistência SQLite e seed inicial.
- `CityPulse.Api/Models/Entities.cs`: entidades Asset/Incident e contratos de entrada.
- `CityPulse.Api/wwwroot`: interface NOC responsiva, manifesto de rotas e assets estáticos.

## Escopo entregue

RF01 gestão visual de ativos com CRUD persistente; RF02 polling periódico real; RF03 confirmação após 3 falhas; RF04 dependência pai-filho e falha em cascata; RF05 MTTR e disponibilidade; RF06 painel NOC com alertas e tipo de problema. RNF02 é atendido por `Parallel.ForEachAsync`; RNF04 por SQLite.
