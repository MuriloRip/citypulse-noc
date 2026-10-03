# Prompt de evolução do CityPulse — foco no Indicador 10.04

Você está trabalhando em um projeto existente chamado **CityPulse**, já parcialmente implementado em C# com ASP.NET Core .NET 8, banco SQLite/Entity Framework Core, monitoramento real por ICMP e HTTP, interface web de NOC, inventário de ativos, triagem operacional, persistência de checks, cálculo de disponibilidade e endpoint de indicador.

Não reconstrua o projeto do zero. Não remova funcionalidades estáveis sem justificar. Primeiro inspecione o código existente, o banco, a interface, o README, o plano e os testes. Depois faça uma evolução incremental, preservando o que já funciona.

O desafio do Ideathon é:

> **Tempo médio de inatividade da infraestrutura de TI da cidade — SEADM — Indicador 10.04 da ISO 37120.**

A situação atual é que o município não possui método de coleta. O CityPulse deve resolver exatamente esse problema: criar um método automático, confiável e auditável para descobrir quando serviços de TI ficam indisponíveis, quanto tempo permanecem fora do ar, quando retornam e quais serviços públicos são afetados.

## Decisão central de escopo

Reduza o foco do produto para a **infraestrutura de TI municipal**.

O CityPulse não deve ser apresentado como sistema de operação da rede elétrica, diagnóstico de transformadores, atendimento da Copel ou plataforma de distribuição de energia. Não implementar agora integração com Copel, portal para concessionária, diagnóstico de raio, transformador, sensores elétricos ou automação de chamados de energia.

A energia pode continuar existindo apenas como uma classificação secundária de causa externa, usada para não atribuir incorretamente um incidente à equipe de TI. Ela não deve ocupar o centro da interface, do pitch ou do fluxo principal.

Se já existirem os estados ou opções `POWER_OUTAGE`, `NO_POWER` ou similares, preserve a compatibilidade técnica se isso evitar migração arriscada, mas:

- rebaixe essa opção para “causa externa”;
- retire-a da narrativa principal;
- não crie integração com concessionária;
- não afirme que o CityPulse resolve falta de energia;
- mantenha a causa como contexto do incidente de TI.

## O que já deve ser considerado existente

Considere que estas partes já foram implementadas ou parcialmente implementadas e devem ser auditadas antes de qualquer alteração:

- backend ASP.NET Core em C#/.NET 8;
- Entity Framework Core;
- banco SQLite para laboratório;
- monitoramento real ICMP e HTTP;
- whitelist e proteção contra alvos não autorizados;
- inventário de ativos;
- dependências entre ativos;
- status online, degradado, triagem, offline e inalcançável;
- polling periódico;
- persistência de checks;
- `TotalChecks`, `SuccessfulChecks` e `UptimePercent`;
- incidentes com início, resolução e duração;
- triagem operacional;
- cascata de dependências;
- endpoint `GET /api/indicator`;
- interface web do NOC;
- cartão do Indicador 10.04;
- manifesto de rotas;
- documentação existente;
- laboratório com ativos e IPs simulados.

Antes de codificar, confira quais itens realmente existem, quais estão incompletos e quais têm inconsistências entre backend e frontend.

## Objetivo da evolução

Transformar o CityPulse em uma demonstração clara, convincente e coerente de uma plataforma municipal de medição de indisponibilidade de TI.

A história do produto deve ser:

```text
Cadastrar ou descobrir ativos autorizados
        ↓
Monitorar automaticamente
        ↓
Detectar uma indisponibilidade
        ↓
Registrar início e causa observada
        ↓
Acompanhar a recuperação
        ↓
Calcular duração e disponibilidade
        ↓
Consolidar o Indicador 10.04
        ↓
Apoiar decisões da SEADM
```

## Escopo funcional prioritário

### 1. Inventário de TI municipal

Organize os ativos por categorias relevantes:

- sistemas administrativos;
- saúde;
- educação;
- arrecadação;
- portal municipal;
- servidores;
- APIs;
- links de internet;
- roteadores;
- switches;
- firewalls;
- Wi-Fi público;
- serviços críticos de prédios públicos.

Cada ativo deve ter:

- nome;
- IP, hostname ou URL;
- protocolo de teste;
- categoria;
- secretaria responsável;
- unidade/local;
- criticidade;
- SLA;
- dependência pai;
- responsável técnico;
- janela de manutenção;
- status atual.

A interface deve deixar claro quais ativos são reais, quais são de laboratório e quais aguardam aprovação.

### 2. Monitoramento automático

O sistema deve executar verificações periódicas e guardar o resultado de cada uma.

Para cada check, persistir:

- ativo;
- horário UTC;
- sucesso ou falha;
- latência;
- protocolo;
- código HTTP ou resultado ICMP;
- causa observada;
- sonda responsável;
- correlação com incidente.

Diferenciar:

- serviço online;
- serviço degradado;
- timeout;
- DNS indisponível;
- porta recusada;
- HTTP 4xx;
- HTTP 5xx;
- ICMP sem resposta;
- serviço em manutenção;
- dependência indisponível.

Não transformar uma única falha transitória automaticamente em incidente crítico. Usar falhas consecutivas, janela de confirmação e política documentada.

### 3. Incidentes e tempo de inatividade

Quando a política de falhas for atingida:

1. registrar o início da indisponibilidade;
2. criar ou atualizar um incidente;
3. identificar os ativos afetados;
4. marcar o impacto nas dependências;
5. alertar o operador de TI;
6. acompanhar novos checks;
7. registrar o primeiro retorno válido;
8. calcular a duração;
9. encerrar o incidente;
10. atualizar os indicadores.

Evitar incidentes duplicados para a mesma queda. Se um incidente continuar aberto, novos checks devem ser eventos do mesmo incidente, não novos incidentes independentes.

### 4. Triagem enxuta

A triagem deve continuar, mas com foco em TI.

Pergunta principal:

```text
Identificamos indisponibilidade no ativo {NOME}.
Qual situação melhor descreve o evento?

1 — Falha de aplicação ou serviço
2 — Falha de servidor ou equipamento
3 — Falha de rede ou link
4 — Manutenção programada
5 — Causa externa ou desconhecida
6 — Serviço já voltou ao normal
```

Mapeamento:

- falha de aplicação → incidente de aplicação;
- servidor/equipamento → incidente de infraestrutura;
- rede/link → incidente de conectividade;
- manutenção → não contabilizar como indisponibilidade não planejada;
- causa externa → registrar como contexto, sem afirmar diagnóstico;
- voltou ao normal → executar novo check e encerrar se confirmado.

A triagem deve registrar responsável, horário, resposta, evidências e ação tomada.

### 5. Dependências e impacto

Manter a topologia porque ela é importante para explicar a complexidade do projeto.

Quando um ativo raiz cair:

- os dependentes devem ser marcados como afetados ou inalcançáveis;
- o sistema deve evitar contar cada dependente como uma nova causa raiz;
- a interface deve separar “incidente raiz” de “impacto propagado”;
- a disponibilidade deve permitir análise por serviço e por infraestrutura;
- a cascata deve funcionar em vários níveis.

Exemplo:

```text
Link municipal
    ↓
Firewall
    ↓
Servidor de saúde
    ↓
Sistema de agendamento
```

Se o link cair, o sistema deve mostrar uma causa raiz e três impactos, não quatro falhas independentes.

## Indicador 10.04 e métricas

O painel deve mostrar claramente o objetivo do desafio, sem esconder o cálculo atrás de uma tela técnica.

Apresentar:

- tempo médio de inatividade;
- indisponibilidade total;
- disponibilidade percentual;
- MTTR;
- quantidade de incidentes;
- incidentes ativos;
- checks realizados;
- ativos monitorados;
- serviços críticos afetados;
- disponibilidade por secretaria;
- disponibilidade por categoria;
- tendência diária, semanal e mensal.

Usar as fórmulas:

```text
Tempo médio de inatividade
= tempo total de indisponibilidade / número de incidentes

Disponibilidade (%)
= (tempo total observado - tempo indisponível) / tempo total observado × 100

MTTR
= tempo total de recuperação / incidentes encerrados
```

Documentar como tratar:

- incidentes ativos;
- manutenção programada;
- falhas transitórias;
- impacto de dependências;
- períodos sem dados;
- janela de apuração.

O endpoint do indicador deve retornar dados completos e coerentes com a interface. No mínimo:

```json
{
  "indicator": "ISO 37120 10.04",
  "period": "monthly",
  "availabilityPercent": 99.2,
  "downtimeMinutes": 340,
  "incidentCount": 12,
  "activeIncidents": 1,
  "meanDowntimeMinutes": 28.3,
  "mttrMinutes": 24.8,
  "totalChecks": 18420,
  "affectedServices": 3,
  "formula": "tempo total de indisponibilidade / número de incidentes"
}
```

Não exibir números fictícios como se fossem medição real. Para o laboratório, usar uma etiqueta clara: **Dados de demonstração**.

## Descoberta de rede

Manter a ideia de descoberta, mas tratá-la como módulo de apoio ao inventário, não como o centro do produto.

O fluxo deve ser:

1. operador informa uma faixa autorizada;
2. sistema valida CIDR e limites;
3. sonda testa somente a rede permitida;
4. resultados aparecem como candidatos;
5. operador aprova os ativos;
6. ativos aprovados entram no inventário;
7. operador define categoria, criticidade e dependências;
8. monitoramento começa após aprovação.

O laboratório deve simular uma infraestrutura privada para a apresentação, por exemplo:

```text
10.44.0.1   Gateway
10.44.0.2   Firewall
10.44.10.1  Switch municipal
10.44.12.10 Unidade de saúde
10.44.24.20 Escola
10.44.30.5  Sistema de saúde
10.44.40.5  Portal municipal
```

A interface deve diferenciar visualmente:

- descoberta real autorizada;
- laboratório simulado;
- candidato ainda não aprovado;
- ativo monitorado.

Nunca permitir varredura irrestrita de IPs públicos.

## Interface e experiência

A interface atual deve ser refinada para parecer uma ferramenta institucional utilizável por uma equipe de TI, não uma tela feita apenas para impressionar.

Prioridades visuais:

1. deixar o Indicador 10.04 visível logo no início;
2. mostrar o período de apuração;
3. separar disponibilidade, incidentes e impacto;
4. usar textos objetivos;
5. mostrar fonte dos dados;
6. diferenciar laboratório de produção;
7. apresentar causa raiz e impactos;
8. permitir filtrar por secretaria, categoria, criticidade e período;
9. mostrar histórico de disponibilidade;
10. permitir abrir um incidente e ver sua linha do tempo.

A tela inicial deve responder em poucos segundos:

- quantos ativos estão sendo monitorados;
- qual a disponibilidade atual;
- quanto tempo de inatividade foi registrado;
- quantos incidentes estão ativos;
- quais serviços críticos estão afetados;
- qual foi o pior incidente do período;
- qual ação o operador deve tomar.

Trocar textos genéricos por linguagem do desafio. Usar, por exemplo:

> “A SEADM agora possui evidência do tempo de indisponibilidade da TI municipal.”

> “3 incidentes encerrados no período; média de 18 minutos por incidente.”

> “O impacto abaixo é derivado de uma causa raiz de conectividade.”

## Alertas

Priorizar alertas internos para a equipe responsável pela TI municipal:

- painel;
- e-mail;
- webhook;
- Microsoft Teams, se autorizado;
- simulação visual no laboratório.

O WhatsApp pode existir como adaptador futuro, mas não deve ser requisito para o pitch nem ocupar a narrativa principal.

Criar uma abstração de notificações:

```text
NotificationGateway
├── SimulatedChannel
├── EmailChannel
├── WebhookChannel
└── WhatsAppChannel futuro
```

Se um canal externo não estiver configurado, exibir claramente “simulação” ou “não configurado”. Não alegar envio real.

## Segurança e operação real

Preservar e reforçar:

- whitelist de alvos;
- proteção contra SSRF;
- autenticação;
- autorização por perfil;
- auditoria;
- HTTPS;
- secrets fora do código;
- limites de polling;
- logs estruturados;
- health check;
- backup;
- migrações;
- manutenção programada;
- retenção de dados;
- tratamento de indisponibilidade da própria plataforma.

Para produção municipal, substituir SQLite por PostgreSQL ou SQL Server, mantendo SQLite para laboratório.

Criar documentação clara do que a prefeitura precisa fornecer:

- lista de sistemas críticos;
- IPs e URLs autorizados;
- responsáveis;
- secretarias;
- criticidade;
- SLAs;
- janelas de manutenção;
- servidor da sonda;
- regras de firewall;
- banco aprovado;
- canais de alerta;
- política de segurança.

## Testes obrigatórios desta evolução

Antes de concluir:

- executar build do backend;
- validar JavaScript;
- validar manifesto de rotas;
- testar `/api/health`;
- testar `/api/indicator`;
- executar um polling real no laboratório;
- confirmar incremento de checks;
- confirmar falha consecutiva e triagem;
- confirmar retorno e duração do incidente;
- confirmar que manutenção não cria indisponibilidade;
- confirmar cascata de vários níveis;
- confirmar que dependentes não viram causas raiz duplicadas;
- confirmar whitelist e bloqueio de SSRF;
- confirmar descoberta somente em CIDR autorizado;
- confirmar aprovação manual dos candidatos;
- confirmar que a interface apresenta o MTTR e o tempo médio de inatividade;
- confirmar que os números da interface correspondem aos números da API;
- confirmar que o laboratório está identificado como simulação.

## Critérios de aceite da entrega

A evolução estará correta quando:

1. o produto puder ser explicado em uma frase alinhada ao desafio;
2. a tela inicial mostrar diretamente o Indicador 10.04;
3. houver uma coleta automática verificável;
4. cada queda tiver início, fim e duração;
5. o tempo médio de inatividade for calculado com fórmula visível;
6. o MTTR vier de dados persistidos;
7. a banca conseguir acompanhar um incidente do check até a recuperação;
8. a interface diferenciar causa raiz de impacto;
9. a descoberta de IPs estiver restrita e exigir aprovação;
10. a triagem estiver focada em causas de TI;
11. energia aparecer apenas como causa externa contextual;
12. não houver narrativa ou funcionalidade central sobre Copel;
13. o modo laboratório estiver explicitamente identificado;
14. o projeto continuar executável localmente;
15. a documentação explicar como evoluir para uma sonda na rede municipal;
16. nenhuma integração externa for declarada como real sem credenciais e teste comprovado.

## Resultado esperado

Ao terminar, entregue:

- código alterado somente onde necessário;
- build aprovado;
- testes executados;
- documentação atualizada;
- roteiro de demonstração de 3 a 5 minutos;
- lista do que já funciona;
- lista do que depende da prefeitura;
- lista do que ficou como evolução futura;
- explicação curta para a banca.

A resposta final deve destacar que o CityPulse não tenta resolver todos os problemas urbanos. Ele resolve um problema específico e mensurável:

> **Criar o primeiro método automático e auditável do município para medir quando a infraestrutura de TI fica fora do ar, por quanto tempo, quais serviços são afetados e como melhorar a continuidade dos serviços públicos.**
