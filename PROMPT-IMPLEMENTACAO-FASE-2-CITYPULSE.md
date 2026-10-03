# Prompt de implementação — CityPulse Fase 2

Você vai continuar o desenvolvimento de um projeto existente chamado **CityPulse**. Não recrie o projeto do zero e não substitua a arquitetura atual sem necessidade. Primeiro leia o código real, execute os testes existentes e confirme o estado atual.

O objetivo do projeto é atender ao desafio do Ideathon:

> **Tempo médio de inatividade da infraestrutura de TI da cidade — SEADM — Indicador 10.04 da ISO 37120.**

O município atualmente não possui um método automático de coleta. O CityPulse deve registrar quando ativos de TI ficam indisponíveis, quanto tempo permanecem fora do ar, quando retornam e quais serviços são afetados.

## Estado atual confirmado

O repositório já possui:

- ASP.NET Core .NET 8;
- C#;
- Entity Framework Core com SQLite;
- Swagger/OpenAPI;
- `BackgroundService` com polling periódico;
- ICMP/Ping real;
- HTTP/HTTPS real;
- cadastro, edição e exclusão de ativos;
- dependência pai entre ativos;
- estados online, degradado, pending triage, no power, down e unreachable;
- falhas consecutivas para transição de estado;
- triagem ativa com fallback após cinco minutos;
- cascata de dependências em múltiplos níveis;
- incidentes com início, resolução e duração;
- persistência de `TotalChecks`, `SuccessfulChecks` e `UptimePercent`;
- endpoint `GET /api/indicator`;
- endpoint `GET /api/snapshot`;
- interface web de NOC;
- inventário visual;
- topologia visual;
- console de operações;
- cartão do Indicador 10.04;
- manifesto `/manus-routes.json`;
- whitelist básica de alvos;
- build atual aprovado;
- JavaScript sem erro sintático.

No teste da auditoria, a API respondeu corretamente e o indicador retornou dados como:

```json
{
  "indicator": "ISO 37120 10.04",
  "totalChecks": 441,
  "downtimeMinutes": 193,
  "incidentCount": 3,
  "activeIncidents": 1,
  "meanDowntimeMinutes": 64.3,
  "availabilityPercent": 65.71
}
```

Preserve as funcionalidades existentes e mantenha a execução local funcionando.

## Resultado esperado da Fase 2

Transformar o MVP funcional em uma demonstração muito mais convincente e em uma base tecnicamente preparada para um piloto municipal.

O produto deve continuar focado em **observabilidade e medição de TI municipal**. Não ampliar o escopo para Copel, operação da rede elétrica, diagnóstico de transformadores ou outros problemas urbanos. Energia pode permanecer apenas como causa externa contextual de um incidente, sem integração externa.

## Ordem obrigatória de implementação

Execute as etapas nesta ordem, validando cada uma antes de seguir.

---

## Etapa 1 — Corrigir e tornar rigoroso o cálculo do indicador

O cálculo atual utiliza contadores agregados no ativo. Mantenha esses contadores para compatibilidade, mas crie uma tabela detalhada de cada verificação.

Criar a entidade `ProbeResult` com:

- `Id`;
- `AssetId`;
- `CheckedAtUtc`;
- `Success`;
- `Status`;
- `Protocol`;
- `LatencyMs`;
- `FailureReason`;
- `HttpStatusCode` opcional;
- `IncidentId` opcional;
- `ProbeSource` ou `ProbeId`;
- `CreatedAtUtc`.

Criar `DbSet<ProbeResult>` e migration oficial.

A cada ciclo real de polling, persistir um registro para cada probe executado.

Corrigir a semântica de sucesso:

- HTTP 2xx e 3xx: sucesso, conforme configuração;
- HTTP 4xx: degradação ou falha funcional, configurável, mas não tratar automaticamente como disponibilidade plena;
- HTTP 5xx: falha de serviço;
- timeout: indisponível;
- DNS failure: indisponível;
- conexão recusada: indisponível;
- ICMP sem resposta: indisponível;
- ICMP bloqueado: registrar como resultado inconclusivo quando possível, sem afirmar que o host caiu.

Documentar a política adotada.

O indicador deve aceitar um período:

```text
GET /api/indicator?from=2026-10-01&to=2026-10-31&category=Saúde
```

A resposta deve conter pelo menos:

```json
{
  "indicator": "ISO 37120 10.04",
  "from": "2026-10-01T00:00:00Z",
  "to": "2026-10-31T23:59:59Z",
  "availabilityPercent": 99.2,
  "downtimeMinutes": 340,
  "incidentCount": 12,
  "activeIncidents": 1,
  "meanDowntimeMinutes": 28.3,
  "mttrMinutes": 24.8,
  "totalChecks": 18420,
  "successfulChecks": 18290,
  "affectedServices": 3,
  "formula": "tempo total de indisponibilidade / número de incidentes"
}
```

Não contar dependentes cascata como novas causas raiz no indicador principal. Mostrar separadamente:

- incidentes raiz;
- impactos propagados;
- disponibilidade por ativo;
- disponibilidade por secretaria;
- disponibilidade por categoria.

Definir e documentar o tratamento de incidentes abertos, manutenção programada e períodos sem dados.

---

## Etapa 2 — Criar histórico e linha do tempo de incidentes

Adicionar eventos de incidente, sem quebrar a entidade atual.

Criar `IncidentEvent` com:

- incidente;
- timestamp UTC;
- tipo do evento;
- status anterior;
- status novo;
- mensagem;
- origem: probe, operador, sistema ou integração;
- usuário, quando aplicável.

Eventos mínimos:

- primeira falha;
- falha consecutiva;
- incidente aberto;
- triagem iniciada;
- triagem respondida;
- dependência afetada;
- causa classificada;
- recuperação detectada;
- incidente encerrado;
- manutenção iniciada;
- manutenção encerrada.

Criar endpoint:

```text
GET /api/incidents
GET /api/incidents/{id}
GET /api/incidents/{id}/timeline
```

Na interface, permitir clicar em um incidente e visualizar a linha do tempo completa.

---

## Etapa 3 — Criar relatórios e filtros

Adicionar uma área **Indicadores e Relatórios**.

Filtros:

- período;
- secretaria;
- categoria;
- unidade;
- criticidade;
- status;
- causa;
- ativo raiz.

Apresentar:

- disponibilidade;
- tempo total de indisponibilidade;
- tempo médio de inatividade;
- MTTR;
- incidentes por causa;
- incidentes por secretaria;
- serviços mais afetados;
- quantidade de impactos propagados;
- tendência do período.

Criar exportações:

```text
GET /api/reports/availability.csv
GET /api/reports/incidents.csv
```

Se PDF ainda não for seguro nesta etapa, fornecer relatório HTML imprimível e documentar PDF como evolução posterior.

A interface deve mostrar claramente o período e a origem dos dados. Não apresentar dados de laboratório como se fossem municipais reais.

---

## Etapa 4 — Tornar a topologia realmente dinâmica

A topologia atual possui nós visuais estáticos. Substitua isso por uma renderização baseada em `snapshot.assets`.

Requisitos:

- desenhar todos os ativos cadastrados;
- respeitar `ParentId`;
- suportar vários níveis;
- identificar raiz ou raízes;
- mostrar status atual;
- mostrar causa raiz;
- mostrar dependentes impactados;
- mostrar caminho de dependência;
- não limitar a topologia aos nós escritos no HTML;
- exibir estado vazio quando não houver ativos;
- exibir “Laboratório” quando os ativos forem simulados.

Pode usar DOM/CSS, SVG ou uma biblioteca leve já compatível com o projeto. Não introduza uma dependência grande sem necessidade.

---

## Etapa 5 — Implementar descoberta autorizada de rede

Criar o módulo **Descoberta de Infraestrutura**, sem permitir varredura irrestrita.

Criar configuração de redes autorizadas, por exemplo:

```json
{
  "authorizedNetworks": [
    "10.44.0.0/16",
    "192.168.100.0/24"
  ]
}
```

Nunca permitir que uma rede seja autorizada apenas por um campo enviado pelo navegador.

Criar:

- `NetworkDiscovery`;
- `DiscoveryCandidate`;
- endpoint para iniciar descoberta;
- endpoint para consultar resultados;
- endpoint para aprovar candidato;
- endpoint para ignorar candidato.

Fluxo:

1. operador escolhe uma rede já autorizada;
2. backend valida o CIDR;
3. descoberta executa somente dentro dos limites configurados;
4. resultados são candidatos;
5. operador aprova individualmente ou em lote;
6. somente ativos aprovados entram no monitoramento;
7. operador define categoria, criticidade e dependências.

A descoberta deve ser segura:

- limite de endereços por varredura;
- timeout;
- concorrência limitada;
- auditoria;
- cancelamento;
- sem shell ping;
- sem varredura de internet pública;
- sem portas não autorizadas.

Criar modo de laboratório com IPs privados simulados:

```text
10.44.0.1   Gateway
10.44.0.2   Firewall
10.44.10.1  Switch municipal
10.44.12.10 Unidade de saúde
10.44.24.20 Escola
10.44.30.5  Sistema de saúde
10.44.40.5  Portal municipal
```

O modo simulado deve ser marcado na interface como:

```text
LABORATÓRIO — DADOS SIMULADOS
```

---

## Etapa 6 — Restringir e auditar alvos

A whitelist atual aceita blocos privados amplos. Substitua isso por duas camadas:

1. bloqueio técnico contra SSRF e alvos públicos não autorizados;
2. lista administrativa de CIDRs e domínios autorizados pela prefeitura.

Não permitir cadastrar qualquer IP privado somente porque ele está em uma faixa privada.

Registrar:

- quem cadastrou o ativo;
- quem alterou endereço;
- quem iniciou descoberta;
- quem aprovou candidato;
- quem excluiu ativo;
- quem iniciou triagem;
- quem resolveu incidente.

Criar `AuditEvent` mesmo que a autenticação completa seja implementada na etapa seguinte.

---

## Etapa 7 — Autenticação e perfis mínimos

Adicionar autenticação adequada para piloto. Não deixar cadastro, edição e exclusão abertos.

Perfis:

- Operador NOC;
- Gestor SEADM;
- Administrador técnico;
- Auditor somente leitura.

Permissões mínimas:

- operador: visualizar, iniciar triagem e atuar incidentes;
- gestor: visualizar relatórios e indicadores;
- administrador: gerenciar ativos, redes e configurações;
- auditor: visualizar histórico e auditoria.

Se uma autenticação completa exigir uma dependência externa, implemente uma solução local claramente documentada para o piloto e mantenha a arquitetura preparada para OIDC/AD/Entra ID.

---

## Etapa 8 — Melhorar a interface sem reescrever o produto

Preserve a identidade visual institucional, mas corrija a aparência de protótipo.

A tela inicial precisa mostrar imediatamente:

- Indicador 10.04;
- período de apuração;
- disponibilidade;
- tempo médio de inatividade;
- MTTR;
- incidentes ativos;
- checks realizados;
- ativos monitorados;
- serviços críticos afetados;
- origem dos dados: laboratório ou monitoramento autorizado.

Adicionar:

- filtros;
- estado de carregamento;
- estado de erro da API;
- estado vazio;
- última atualização;
- link para detalhes do incidente;
- linha do tempo;
- indicação de causa raiz e impacto;
- legenda de laboratório.

Remover números estáticos de exemplo que aparecem antes do carregamento. O HTML deve iniciar com valores neutros ou skeletons e ser preenchido pela API.

Corrigir textos para que o foco seja TI municipal. A energia deve aparecer somente como “causa externa” ou categoria contextual, não como eixo principal.

---

## Etapa 9 — Alertas internos

Criar uma abstração:

```text
NotificationGateway
├── SimulatedChannel
├── EmailChannel
└── WebhookChannel
```

Implementar primeiro:

- alerta visual no painel;
- registro de notificação;
- webhook configurável ou modo simulado;
- e-mail como opção documentada.

Não implementar Copel nesta fase.

Não declarar WhatsApp real. Se houver uma simulação no console, rotular como:

```text
SIMULAÇÃO DE NOTIFICAÇÃO
```

---

## Etapa 10 — Banco, migrations e produção

Substituir alterações manuais de schema no startup por migrations do EF Core.

Manter SQLite para laboratório, mas permitir configuração de:

- SQLite em desenvolvimento;
- PostgreSQL ou SQL Server em produção.

Usar connection string por configuração segura. Não deixar secrets no repositório.

Documentar:

- instalação local;
- criação do banco;
- migrations;
- seed de laboratório;
- seed vazio para produção;
- backup;
- restauração;
- logs;
- execução como Windows Service;
- execução como daemon Linux;
- health check;
- atualização;
- rollback.

---

## Testes obrigatórios

Criar ou ampliar testes para:

- cálculo de disponibilidade por período;
- cálculo do tempo médio de inatividade;
- MTTR;
- incidente aberto;
- incidente encerrado;
- incidente ativo;
- manutenção programada;
- falha transitória;
- 3 falhas consecutivas;
- recuperação;
- cascata em três níveis;
- distinção entre causa raiz e dependente;
- HTTP 2xx;
- HTTP 4xx;
- HTTP 5xx;
- timeout;
- DNS failure;
- ICMP sem resposta;
- whitelist administrativa;
- SSRF;
- CIDR não autorizado;
- limite de descoberta;
- aprovação de candidato;
- duplicidade de ativo;
- exportação CSV;
- permissões;
- auditoria;
- banco vazio;
- banco com dados de laboratório.

Além dos testes automatizados, executar:

```text
 dotnet build --configuration Release
 node --check CityPulse.Api/wwwroot/app.js
 git diff --check
```

Subir a API localmente e validar:

```text
GET /api/health
GET /api/assets
GET /api/snapshot
GET /api/indicator
GET /manus-routes.json
```

Executar pelo menos um polling real e confirmar incremento de checks.

---

## Critérios de aceite da Fase 2

A entrega será aceita quando:

1. o build passar sem warnings ou erros;
2. cada probe executado puder ser consultado no histórico;
3. o indicador aceitar período e filtros;
4. a interface mostrar MTTR e tempo médio de inatividade coerentes com a API;
5. incidentes tiverem linha do tempo;
6. a topologia renderizar todos os ativos dinamicamente;
7. a cascata separar causa raiz de impacto;
8. houver relatório CSV ou HTML imprimível;
9. a descoberta funcionar somente em redes autorizadas;
10. candidatos precisarem de aprovação;
11. o laboratório estiver visualmente identificado;
12. o backend registrar auditoria das ações importantes;
13. cadastro e exclusão não ficarem abertos sem autenticação;
14. o banco puder migrar sem `ALTER TABLE` ad hoc no startup;
15. a documentação explicar instalação e operação;
16. a narrativa do produto estiver centrada na medição da indisponibilidade de TI municipal;
17. não houver integração ou promessa indevida com Copel;
18. os números apresentados na interface não forem estáticos ou fictícios sem identificação.

## Resposta final esperada da IA

Ao concluir, informe:

1. quais arquivos foram alterados;
2. quais funcionalidades foram implementadas;
3. quais testes foram executados e seus resultados;
4. como iniciar o projeto;
5. como executar o laboratório;
6. como demonstrar o Indicador 10.04;
7. o que ainda depende de informações da prefeitura;
8. o que ficou explicitamente como evolução futura.

A mensagem final para a banca deve ser:

> O CityPulse não é apenas um painel de servidores. É um método automático e auditável para que a prefeitura descubra quando sua infraestrutura de TI fica indisponível, registre a duração real, identifique a causa raiz, conheça os serviços afetados e acompanhe o Indicador 10.04 ao longo do tempo.
