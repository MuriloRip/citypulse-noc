# Prompt-mestre — CityPulse em produção municipal

Você é uma equipe sênior de produto, arquitetura, segurança, UX, desenvolvimento C#/.NET e infraestrutura. Sua missão é transformar o CityPulse em uma plataforma real, segura, auditável e instalável para monitorar a infraestrutura de TI de um município.

Não trate este trabalho como a criação de um dashboard genérico. O problema central é medir de forma confiável o **tempo médio de inatividade da infraestrutura de TI da cidade**, relacionado ao Indicador 10.04 da ISO 37120, para apoiar a SEADM — Secretaria de Administração.

Não invente integrações, dados, credenciais, APIs ou validações. Quando algo depender de autorização externa, documente a dependência, crie uma interface de integração e um modo de simulação claramente identificado.

---

## 1. Objetivo do produto

Criar uma plataforma que:

1. descubra ou receba ativos autorizados da infraestrutura municipal;
2. monitore automaticamente sistemas, servidores, links, roteadores, switches, prédios públicos e serviços críticos;
3. registre cada verificação, falha, retorno e duração da indisponibilidade;
4. diferencie indisponibilidade de TI, falta de energia e interrupção temporária;
5. faça triagem com o responsável local;
6. acione a equipe correta com base na causa provável;
7. evite abrir falsos chamados para a TI ou para a concessionária de energia;
8. calcule o tempo médio de inatividade, disponibilidade, MTTR e outros indicadores;
9. apresente dados auditáveis em painel e relatórios;
10. possa ser instalado em um servidor da prefeitura e executado continuamente.

A frase principal do produto é:

> O CityPulse transforma indisponibilidade invisível em evidência operacional: detecta, mede, classifica, encaminha e comprova quanto tempo a infraestrutura de TI municipal ficou indisponível.

---

## 2. Escopo da infraestrutura monitorada

Criar um inventário organizado por categorias:

- sistemas administrativos da prefeitura;
- Portal do Cidadão;
- sistemas de saúde;
- sistemas de educação;
- sistemas de arrecadação;
- servidores físicos e virtuais;
- bancos de dados, quando houver permissão;
- links de internet dos prédios públicos;
- roteadores, firewalls e switches;
- rede de UBS, escolas, secretarias e unidades operacionais;
- Wi-Fi público, se houver;
- APIs e serviços internos;
- equipamentos críticos autorizados;
- energia elétrica associada à continuidade da TI, quando houver sensor ou confirmação humana.

Cada ativo deve ter, no mínimo:

- nome;
- endereço IP, hostname ou URL;
- protocolo de monitoramento;
- categoria;
- secretaria responsável;
- unidade ou prédio;
- endereço físico;
- latitude e longitude, se disponíveis;
- criticidade: baixa, média ou alta;
- SLA esperado;
- ativo pai e dependências;
- responsável operacional;
- janela de manutenção;
- data de entrada no monitoramento;
- estado atual;
- histórico de checks;
- histórico de incidentes.

Não cadastrar automaticamente tudo que responder. A descoberta deve gerar ativos pendentes para revisão e aprovação do operador.

---

## 3. Arquitetura obrigatória

Usar uma arquitetura separada em componentes:

```text
Sonda CityPulse na rede municipal
        ↓
ICMP / TCP / HTTP / HTTPS / DNS / SNMP
        ↓
API central ASP.NET Core
        ↓
Motor de correlação e incidentes
        ↓
PostgreSQL ou SQL Server
        ↓
Painel web, alertas e relatórios
```

### Backend

- C# com ASP.NET Core .NET 8 ou superior;
- API REST documentada com OpenAPI/Swagger;
- Entity Framework Core;
- PostgreSQL ou SQL Server para produção;
- SQLite apenas para demonstração local ou instalação muito pequena;
- Worker Service ou BackgroundService para polling;
- filas internas para alertas e integrações;
- `CancellationToken` em todas as operações longas;
- limite de concorrência configurável;
- retry com backoff;
- timeout por tipo de probe;
- logs estruturados;
- health checks;
- métricas internas;
- migrations oficiais do Entity Framework.

### Sonda

A sonda deve rodar dentro da rede municipal, porque somente ela conseguirá testar IPs privados e equipamentos internos.

A sonda deve:

- ter identidade própria;
- autenticar-se na API central;
- usar HTTPS;
- receber apenas as redes autorizadas;
- enviar resultados assinados ou autenticados;
- funcionar mesmo com perda temporária de conexão com a API;
- armazenar temporariamente resultados localmente;
- reenviar os dados quando a conexão voltar;
- possuir logs locais;
- não executar comandos shell de ping;
- respeitar limites de segurança e concorrência.

### Frontend

Criar uma interface web institucional de NOC, não um dashboard gamer.

Usar:

- React, Vue ou frontend compatível com o projeto existente;
- TypeScript quando possível;
- componentes acessíveis;
- contraste adequado;
- navegação por teclado;
- estados claros;
- responsividade;
- atualização em tempo real por SignalR ou polling controlado;
- mensagens factuais e operacionais.

---

## 4. Descoberta de infraestrutura autorizada

Criar um módulo chamado **Descoberta de Infraestrutura**.

O operador deve informar uma rede previamente autorizada, por exemplo:

```text
Rede: 10.44.0.0/24
Sonda: Sonda Paço Municipal
Métodos: ICMP + TCP + DNS
```

Nunca permitir varredura irrestrita da internet.

O módulo deve:

1. validar se a faixa está na lista autorizada;
2. calcular o número máximo de endereços;
3. impedir redes grandes demais sem autorização adicional;
4. registrar quem iniciou a descoberta;
5. registrar data, hora e motivo;
6. encontrar hosts que respondem;
7. tentar DNS reverso;
8. identificar MAC quando a sonda puder usar ARP autorizado;
9. detectar portas explicitamente permitidas;
10. identificar HTTP/HTTPS;
11. coletar fabricante apenas quando tecnicamente possível;
12. marcar o resultado como candidato;
13. permitir aprovação manual;
14. criar o ativo somente após aprovação;
15. permitir definir pai, categoria, criticidade e responsável.

Resultado esperado:

```text
IP              Nome                  Tipo provável       Resposta       Ação
10.44.0.1       gateway-paço          gateway             ICMP            aprovar
10.44.12.10    desconhecido           host                ICMP            revisar
10.44.30.5     saude-api              servidor HTTP       HTTP 200        aprovar
10.44.40.20    equipamento-x          desconhecido        sem resposta   ignorar
```

A descoberta deve ter modo demonstrativo usando uma infraestrutura simulada de IPs privados. Esse modo deve aparecer claramente como **LABORATÓRIO / SIMULAÇÃO**, nunca como dado real.

---

## 5. Monitoramento e máquina de estados

Implementar os estados:

```text
ONLINE
DEGRADED
PENDING_TRIAGE
NO_POWER
DOWN
UNREACHABLE
MAINTENANCE
UNKNOWN
```

Fluxo principal:

```text
ONLINE
  ↓ falhas consecutivas acima do limite
PENDING_TRIAGE
  ├── POWER_OUTAGE → NO_POWER
  ├── NETWORK_FAULT → DOWN
  ├── RESTORE → ONLINE
  └── sem resposta no prazo → DOWN
```

Regras:

- o limite de falhas deve ser configurável;
- o padrão pode ser 3 falhas consecutivas;
- o tempo de triagem deve ser configurável;
- o padrão pode ser 5 minutos;
- `PENDING_TRIAGE` não deve ser tratado automaticamente como falha técnica definitiva;
- `NO_POWER` não deve acionar SLA de TI;
- `NO_POWER` não deve derrubar filhos automaticamente;
- `NETWORK_FAULT` deve acionar incidente técnico e dependências;
- `DOWN` deve criar ou atualizar incidente;
- `UNREACHABLE` deve indicar impacto de dependência;
- `MAINTENANCE` não deve gerar falso incidente;
- retorno ao estado normal deve registrar o horário de recuperação.

### Tipos de probe

Implementar, conforme o tipo do ativo:

- ICMP;
- TCP em portas autorizadas;
- HTTP/HTTPS;
- DNS;
- SNMP somente com credenciais e permissão;
- health endpoint específico;
- certificado TLS próximo do vencimento;
- latência;
- perda de pacotes, quando disponível.

Não considerar um HTTP 404 igual a um timeout de rede. Registrar a causa observada:

- DNS failure;
- timeout;
- conexão recusada;
- ICMP bloqueado;
- HTTP 4xx;
- HTTP 5xx;
- certificado inválido;
- autenticação recusada;
- dependência indisponível;
- host sem resposta.

---

## 6. Diferenciar queda temporária, energia e falha de TI

Criar uma classificação por evidências, sem afirmar uma causa que não foi confirmada.

### Interrupção momentânea

Classificar como interrupção momentânea quando:

- a indisponibilidade durar menos que o limite configurado;
- o serviço retornar sozinho;
- não houver confirmação de falta de energia;
- não houver impacto correlacionado em outras unidades.

Registrar para estatística, mas não abrir automaticamente chamado externo.

### Possível falta de energia

Aumentar a confiança quando houver:

- resposta do responsável local “sem energia”;
- sensor de energia, nobreak ou PDU indicando queda;
- múltiplos equipamentos do mesmo prédio indisponíveis;
- várias unidades próximas caindo no mesmo horário;
- histórico de duração persistente;
- informação oficial de manutenção ou interrupção.

Nunca classificar “transformador queimado”, “raio” ou “rompimento de rede” sem evidência. Usar “possível causa” e registrar a fonte.

### Falha de rede ou equipamento

Classificar como falha técnica quando:

- energia local foi confirmada;
- o responsável confirma falha de equipamento;
- apenas um equipamento ou serviço está indisponível;
- outros equipamentos do mesmo local continuam respondendo;
- existe erro de rota, porta, DNS ou serviço;
- a falha afeta dependentes conhecidos.

Criar um `confidenceScore` e uma lista de evidências:

```json
{
  "classification": "POWER_OUTAGE",
  "confidenceScore": 0.92,
  "evidence": [
    "responsavel confirmou sem energia",
    "tres equipamentos do mesmo predio indisponiveis",
    "queda persistente por 8 minutos"
  ]
}
```

---

## 7. Bot de triagem

Criar uma camada de integração chamada **Notification Gateway**.

O sistema deve funcionar em três modos:

1. simulação local para o Ideathon;
2. e-mail ou Teams para piloto;
3. WhatsApp Business API ou outro canal autorizado para produção.

O bot deve se identificar claramente como automatizado. Nunca fingir ser uma pessoa.

Mensagem inicial:

```text
[CityPulse Bot — SEADM]

Olá. Esta é uma mensagem automática de monitoramento da infraestrutura municipal.

Detectamos ausência de comunicação com a unidade {NOME}, localizada em {ENDEREÇO}, às {HORÁRIO}.

A infraestrutura de TI do local está afetada?

Responda:
1 — Sem energia elétrica no local
2 — Falha nos equipamentos ou na rede
3 — Serviço funcionando normalmente
4 — Não consigo verificar

Esta mensagem é automática. Em caso de emergência, utilize os canais oficiais da prefeitura.
Protocolo: {PROTOCOLO}
```

Mapeamento:

- resposta 1 → `POWER_OUTAGE`;
- resposta 2 → `NETWORK_FAULT`;
- resposta 3 → `RESTORE` ou nova verificação;
- resposta 4 → manter triagem e escalar;
- sem resposta → fallback para `DOWN` após o prazo configurado.

Registrar:

- canal;
- destinatário;
- horário do envio;
- horário da resposta;
- conteúdo recebido;
- operador ou responsável;
- protocolo;
- estado anterior e posterior;
- evidências utilizadas.

---

## 8. Portal de ocorrências para concessionária

Criar o conceito de **Fila de Ocorrências de Energia**, sem afirmar integração oficial até haver autorização da concessionária.

A fila deve receber somente ocorrências com evidência suficiente e conter:

- protocolo CityPulse;
- prioridade;
- unidade;
- endereço;
- coordenadas;
- horário estimado da queda;
- duração;
- última resposta;
- confirmação do responsável;
- equipamentos afetados;
- quantidade de unidades impactadas;
- confiança da classificação;
- evidências;
- possível causa, sem afirmar diagnóstico;
- status do encaminhamento;
- protocolo externo, quando existir.

Níveis:

- observando;
- provável interrupção;
- confirmada pelo responsável;
- pronta para encaminhar;
- enviada;
- em atendimento;
- resolvida;
- rejeitada por falta de evidência.

Para o MVP, gerar uma mensagem pronta ou e-mail institucional. Para produção, criar um adaptador separado para o canal oficial disponibilizado pela concessionária.

---

## 9. Banco de dados e indicador

Usar migrations oficiais. Não depender somente de `EnsureCreated` ou alterações silenciosas em produção.

Entidades mínimas:

- Asset;
- Probe;
- ProbeResult;
- Incident;
- IncidentEvent;
- TriageSession;
- TriageMessage;
- Evidence;
- MaintenanceWindow;
- Notification;
- PowerOccurrence;
- NetworkDiscovery;
- DiscoveryCandidate;
- AuditEvent;
- User;
- Role;
- Sonda.

Guardar timestamps em UTC.

Cada check deve ter:

- ativo;
- sonda;
- horário de início;
- horário de fim;
- sucesso;
- status HTTP ou ICMP;
- latência;
- motivo da falha;
- versão do agente;
- correlação com incidente.

Fórmulas:

```text
Tempo médio de inatividade
= tempo total de indisponibilidade / quantidade de incidentes

Disponibilidade percentual
= (tempo total observado - tempo indisponível) / tempo total observado × 100

MTTR
= soma do tempo de recuperação dos incidentes encerrados / quantidade de incidentes encerrados
```

Definir explicitamente:

- janela de apuração diária, semanal e mensal;
- tratamento de incidentes ainda abertos;
- manutenção programada;
- incidentes causados por energia;
- dependências e indisponibilidade correlacionada;
- duplicidade de incidentes;
- horário de funcionamento, se aplicável.

Expor:

- `/api/indicator`;
- `/api/reports/availability`;
- `/api/reports/incidents`;
- exportação CSV;
- relatório PDF ou HTML;
- filtros por período, secretaria, unidade, categoria e criticidade.

---

## 10. Interface obrigatória

Criar as áreas:

### Visão executiva

- disponibilidade municipal;
- tempo médio de inatividade;
- MTTR;
- incidentes ativos;
- ativos monitorados;
- checks realizados;
- tendência comparada ao período anterior;
- principais unidades afetadas;
- resumo do período.

### Inventário

- busca;
- filtros;
- cadastro;
- edição;
- aprovação de descoberta;
- manutenção;
- criticidade;
- responsável;
- dependências.

### Topologia

- mapa de dependências;
- status por nó;
- impacto cascata;
- caminho até a raiz;
- relação entre sonda, gateway, rede e serviço.

### Console de Operações

- triagem pendente;
- timer;
- conversa do bot;
- evidências;
- botões de resposta;
- confiança da classificação;
- ação recomendada;
- registro de auditoria.

### Indicadores e relatórios

- indicador 10.04;
- disponibilidade por secretaria;
- indisponibilidade por unidade;
- MTTR por categoria;
- incidentes por causa;
- energia versus TI;
- interrupções momentâneas;
- tendência mensal;
- exportação.

A interface deve usar linguagem institucional, contraste acessível e cores com significado:

- verde: online;
- amarelo: aguardando triagem;
- laranja queimado: energia;
- vermelho: falha técnica crítica;
- roxo ou cinza: impacto por dependência.

---

## 11. Segurança

Implementar antes de produção:

- autenticação;
- autorização por perfil;
- MFA quando possível;
- HTTPS obrigatório;
- secrets fora do código;
- rotação de tokens;
- proteção contra SSRF;
- redes permitidas por CIDR;
- limites de descoberta;
- proteção contra varredura externa;
- rate limiting;
- validação de payload;
- logs de auditoria;
- retenção de dados definida;
- backup criptografado;
- princípio do menor privilégio;
- segregação entre operador, administrador e auditor;
- proteção da API da sonda;
- bloqueio de comandos shell arbitrários.

Perfis mínimos:

- Operador NOC;
- Gestor SEADM;
- Administrador técnico;
- Auditor somente leitura;
- Serviço da sonda.

---

## 12. Configuração real que a prefeitura precisa fornecer

Antes de instalar, solicitar:

- lista de sistemas críticos;
- IPs, URLs e portas autorizadas;
- faixas de rede municipais;
- localização dos prédios;
- secretarias e responsáveis;
- contatos de plantão;
- horários de manutenção;
- níveis de criticidade;
- SLAs;
- canal de alerta autorizado;
- servidor onde a sonda será instalada;
- banco de dados aprovado;
- regras de firewall;
- certificado TLS;
- política de retenção;
- política de backup;
- autorização para SNMP;
- autorização para integração externa;
- responsável pelo aceite operacional.

Não usar os dados reais recebidos no código-fonte. Usar variáveis de ambiente, secret manager ou arquivo de configuração protegido.

---

## 13. Implantação

Entregar:

- Docker Compose para laboratório;
- publicação para Windows Server;
- serviço Windows para a sonda;
- script de instalação;
- script de atualização;
- script de backup;
- configuração de firewall;
- health check;
- logs;
- documentação de rollback;
- documentação de recuperação de banco;
- manual do operador;
- manual do administrador;
- diagrama de arquitetura;
- inventário de portas e dependências.

Exemplo de ambientes:

```text
Desenvolvimento → Homologação → Piloto → Produção
```

Cada ambiente deve ter banco separado e credenciais separadas.

---

## 14. Testes obrigatórios

Criar testes automatizados para:

- cálculo de disponibilidade;
- cálculo de MTTR;
- incidente aberto;
- incidente encerrado;
- três falhas consecutivas;
- triagem pendente;
- timeout da triagem;
- falta de energia sem cascata;
- falha de rede com cascata;
- cascata com netos;
- recuperação;
- manutenção programada;
- DNS failure;
- timeout;
- HTTP 404;
- HTTP 500;
- ICMP bloqueado;
- SSRF;
- rede não autorizada;
- descoberta com aprovação manual;
- duplicidade de ativos;
- queda da API;
- perda temporária da conexão da sonda;
- reenvio de resultados;
- backup e restauração;
- permissões de usuário;
- auditoria.

Criar um cenário de laboratório com IPs privados simulados para o pitch:

```text
10.44.0.1   Gateway
10.44.0.2   Firewall
10.44.10.1  Switch principal
10.44.12.10 UBS Centro
10.44.12.11 UBS Jardim
10.44.24.20 Escola Municipal
10.44.30.5  Servidor de Saúde
10.44.40.5  Portal do Cidadão
```

O laboratório deve permitir:

- descobrir candidatos;
- aprovar ativos;
- criar topologia;
- simular queda do gateway;
- simular falta de energia;
- simular falha de rede;
- mostrar triagem;
- mostrar cascata;
- mostrar relatório;
- restaurar a operação.

---

## 15. Critérios de aceite

O projeto só será considerado pronto quando:

1. um ativo autorizado puder ser cadastrado;
2. uma sonda puder monitorá-lo continuamente;
3. cada check for armazenado;
4. uma falha criar histórico e incidente de acordo com as regras;
5. o sistema medir o início e o fim da indisponibilidade;
6. o indicador 10.04 for calculado por fórmula documentada;
7. o painel mostrar o MTTR real;
8. o operador puder iniciar e resolver uma triagem;
9. falta de energia não derrubar dependências;
10. falha de rede derrubar dependências;
11. o fallback funcionar sem resposta;
12. a cascata funcionar em vários níveis;
13. a descoberta exigir rede autorizada;
14. candidatos só entrarem após aprovação;
15. o bot registrar mensagens e respostas;
16. notificações não forem alegadas como reais sem integração configurada;
17. relatórios puderem ser exportados;
18. usuários e permissões funcionarem;
19. backup e restauração forem testados;
20. a instalação em um servidor municipal estiver documentada.

---

## 16. Regras de comunicação com a banca

Nunca dizer:

- que o WhatsApp já está integrado se não houver credencial e teste real;
- que a Copel recebe chamados automaticamente se não houver canal autorizado;
- que a descoberta escaneia a rede municipal se a sonda não estiver instalada lá;
- que dados de laboratório são dados reais;
- que uma causa foi confirmada sem evidência.

Dizer:

> O CityPulse já possui o núcleo de coleta, persistência, cálculo, triagem e visualização. No piloto, a prefeitura fornece os ativos autorizados e instala uma sonda dentro da rede municipal. As integrações de WhatsApp, concessionária e sistemas internos são adaptadores configuráveis, ativados somente após autorização.

A demonstração deve contar esta história:

```text
Descobrir
  ↓
Aprovar
  ↓
Monitorar
  ↓
Detectar
  ↓
Triar
  ↓
Classificar
  ↓
Acionar
  ↓
Registrar
  ↓
Calcular o indicador
  ↓
Apoiar decisão da SEADM
```

Ao implementar, entregue código organizado, documentação de configuração, testes, dados de laboratório separados dos dados reais, migrações, instruções de instalação e uma lista clara do que depende da prefeitura.
