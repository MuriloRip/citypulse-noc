# CityPulse — direção do produto

## Posicionamento

Plataforma genérica de operações para monitorar ativos, serviços e relações de dependência. Não é vinculada a município, setor ou organização específica. Cada instalação mantém seu próprio inventário e configuração.

## Princípios da experiência

- Interface de operações profissional e sóbria, com hierarquia clara, informação verificável e cor reservada para estados.
- Tornar termos operacionais autoexplicativos e oferecer caminhos guiados sem esconder o escopo e os limites reais da coleta.
- Não apresentar valores demonstrativos ou previsões como telemetria real.
- Separar saúde do ativo, disponibilidade histórica, incidentes e ações do operador.
- Tratar triagem manual como fluxo operacional real, sem simular integrações externas inexistentes.
- Priorizar leitura de teclado, contraste, responsividade e respeito à preferência por movimento reduzido.

## Arquitetura atual

- `CityPulse.Api/Program.cs`: API ASP.NET Core, contratos HTTP, cabeçalhos de segurança e configuração de acesso.
- `CityPulse.Api/Endpoints`: rotas de inventário, operações e descoberta explícita.
- `CityPulse.Api/Security/TargetPolicy.cs`: validação dos destinos de sondagem.
- `CityPulse.Api/Services/MonitoringService.cs`: sondagens ICMP/HTTP, estado, triagem e worker periódico.
- `CityPulse.Api/Services/NetworkDiscoveryService.cs`: sondagens autorizadas e limitadas de IPv4 privado.
- `CityPulse.Api/Data/CityPulseDbContext.cs` e `DatabaseInitializer.cs`: EF Core e persistência SQLite.
- `CityPulse.Api/Models/Entities.cs`: ativos, incidentes e contratos de entrada.
- `CityPulse.Api/wwwroot`: aplicação de interface servida pelo backend.

## Limites e evolução

O monitoramento verifica alvos cadastrados por ICMP/HTTP. A descoberta manual limita-se a IPv4 privado `/24` até `/32`, com confirmação por execução e sondagens ICMP/TCP em portas comuns; a categoria é estimada, não confirma fabricante ou modelo. SNMP, coleta de configuração, análise de tráfego, identidade de usuários e autenticação própria ainda não estão implementados. Integrações novas devem ser explícitas, autorizadas, restritas a leitura quando possível e ter credenciais guardadas fora do banco de inventário.

O sistema ainda não coleta CPU/memória e não tem testes de carga para milhares de ativos. Não apresentar o SQLite ou o polling atual como dimensionados para essa escala; primeiro definir limites, migrar o banco se necessário e validar um plano de coleta com testes de carga.

Antes de um rollout, configurar autenticação, autorização, TLS, firewall e retenção através de uma camada de acesso confiável. O instalador Windows é uma forma de distribuição, não um controle de segurança nem uma mudança no protocolo de rede.
