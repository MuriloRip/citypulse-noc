# CityPulse NOC

Plataforma de monitoramento e inventário de infraestrutura para equipes que precisam acompanhar disponibilidade, incidentes e dependências de ativos em redes autorizadas. Reúne um painel web, sondagens de conectividade e descoberta manual de dispositivos, com revisão do operador antes de incluir resultados no inventário.

O backend usa ASP.NET Core/.NET 8, EF Core e SQLite, e serve a própria interface web. Pode ser executado localmente ou implantado como uma instância compartilhada; as instruções abaixo descrevem instalação, operação, segurança e limites conhecidos.

## Executar localmente

Requer o SDK .NET 8:

```powershell
cd CityPulse.Api
dotnet restore
dotnet run --urls http://127.0.0.1:4173
```

Abra `http://127.0.0.1:4173`. A API usa `citypulse.db` no diretório de execução, criado automaticamente. Para usar outro banco, defina `ConnectionStrings__CityPulse`, por exemplo `Data Source=C:\ProgramData\CityPulse\citypulse.db`. A documentação Swagger é habilitada somente em ambiente Development. `POST /api/poll` dispara uma varredura manual; o serviço também verifica a cada 30 segundos.

O repositório inclui `NuGet.config` com `nuget.org` habilitado. Se o restore informar `NU1100`, confira `dotnet nuget list source` e a conectividade com `https://api.nuget.org/v3/index.json`.

## Aplicativos para download

Ao publicar uma versão em **GitHub Releases**, o workflow de empacotamento anexa:

- `CityPulse-Setup-win-x64.exe`: instalador Windows x64 para executar localmente.
- `CityPulse-linux-x64.tar.gz`: app autocontido Linux x64, adequado a teste local. Extraia e execute `./run-citypulse.sh`.

O workflow também pode ser iniciado manualmente em Actions para validar builds e baixar artefatos de teste. Para uma VM Linux sempre ligada ou acesso por vários técnicos, prefira a implantação Docker abaixo. Esses pacotes executam o backend local; sincronizar aparelhos requer que todos usem a mesma instância central.

Para instalar no celular sem manter um segundo backend, acesse a URL HTTPS da instalação central: Android oferece a opção **Instalar app** no navegador; no iPhone/iPad, use **Compartilhar → Adicionar à Tela de Início** no Safari. Windows, Linux e macOS também podem instalar o site pelo navegador compatível. O modo instalável mantém os mesmos dados e usuários da instância central; sem rede, apenas a interface abre e os dados permanecem indisponíveis.

O painel permite cadastrar/editar/excluir ativos, descobrir e revisar dispositivos em uma faixa privada autorizada, selecionar em lote os resultados novos e adicioná-los ao inventário com uma única ação, filtrar por status, procurar por nome/endereço/categoria/local, exportar CSV, visualizar dependências e acompanhar/resolver triagens. Ilustrações originais indicam a categoria estimada, com um ícone neutro quando não há evidência suficiente. O CSV neutraliza campos que planilhas poderiam interpretar como fórmulas.

O painel também apresenta o indicador 10.04 da ISO 37120 como estimativa operacional, com verificações e períodos de indisponibilidade observados. A implementação não representa certificação ISO: validar definições, período de apuração e evidências com a organização responsável antes de usar os números em relatórios oficiais.

## Escopo de coleta e escala

Um endereço IP cadastrado verifica conectividade por ICMP e uma URL interna verifica resposta HTTP(S). A descoberta manual verifica no máximo 254 endereços em uma faixa IPv4 privada CIDR informada pelo operador: combina ICMP com tentativas TCP breves nas portas 22, 80, 443, 445, 554, 3389 e 9100. É preciso confirmar autorização a cada varredura. Apenas equipamentos que respondem aparecem; as categorias são estimativas explicadas pelas evidências, não identificação garantida. Resultados só entram no inventário quando o operador os adiciona, e não são guardados pelo servidor como uma sessão de descoberta. A descoberta não faz consulta SNMP, leitura de tráfego, busca de fabricante/MAC, coleta de CPU/memória ou alteração de configurações.

A plataforma ainda não coleta uso de CPU ou memória, não oferece SNMP/IPMI nem scripts customizados. O armazenamento é SQLite e ainda não há benchmark que sustente operação com milhares de ativos. Não dimensione a produção para essa escala antes de adicionar banco e agendamento adequados, paginação/retenção e testes de carga.

Para evoluir com segurança: adicionar descoberta por SNMPv3 ou APIs dos fabricantes, somente leitura, com credenciais geridas como segredos; e um agente explícito opcional para métricas de host. Segredos de coleta precisam de armazenamento apropriado, escopo mínimo e controle de acesso, sem scripts remotos arbitrários. PostgreSQL, concorrência, partições e capacidade devem ser validados com ensaios de carga antes de qualquer promessa de escala.

## VM Linux ou uso por vários dispositivos

Docker Compose executa a API numa rede interna, mantém SQLite num volume e publica apenas o Caddy, com TLS automático e autenticação básica:

```sh
cp .env.example .env
chmod 600 .env
docker run --rm -it caddy:2 caddy hash-password --algorithm argon2id
# Coloque o hash impresso em CITYPULSE_AUTH_HASH no .env (preserve os $ entre aspas simples).
# O Caddy lê a senha sem exibi-la no terminal. Substitua o domínio e o hash de exemplo; não use o .env.example diretamente.
# Configure CITYPULSE_DOMAIN com um hostname cujo DNS aponte para esta VM.
docker compose up -d --build
```

Para TLS público, permita que o Caddy alcance/receba as portas 80 e 443 conforme seu DNS e firewall. Para DNS exclusivamente interno, configure `CITYPULSE_CADDYFILE=./deploy/Caddyfile.internal` e um hostname interno em `.env`; essa configuração usa a CA interna do Caddy, que precisa ser confiada nos dispositivos clientes antes do uso. Proteja o arquivo `.env` e os volumes do Docker.

A autenticação básica do exemplo é uma barreira inicial compartilhada, não oferece usuários individuais, autorização por função nem auditoria de identidade. Antes de uso empresarial ou acesso pela internet, integre um proxy OIDC/SSO (por exemplo, um provedor corporativo), defina permissões por perfil e restrinja a aplicação ao proxy. Não compartilhe uma única senha entre técnicos se for necessária responsabilização individual. A API não implementa login próprio nesta etapa.

Para um backup coerente do SQLite, pare temporariamente o container `citypulse` antes de copiar o volume de dados. Proteja o backup como dado de produção e restrinja o acesso ao host da VM.

## Monitoramento e integração de rede

O motor executa sondagens ICMP para IPv4 privado/loopback e HTTP(S) para localhost ou IPv4 privado literal; redirects HTTP são desabilitados. HTTP aceita respostas 2xx/3xx como online, 4xx como degradado e 5xx/falha de transporte como indisponível. O corpo das respostas HTTP não é lido. Há concorrência limitada, timeout e confirmação de falha após tentativas consecutivas.

O produto oferece descoberta manual limitada a uma faixa IPv4 privada CIDR de até 254 endereços, depois da confirmação de autorização. Para cada endereço, testa ICMP e conexões TCP breves somente nas portas 22, 80, 443, 445, 554, 3389 e 9100. Não tenta autenticar, explorar vulnerabilidades, obter dados de configuração ou ler payloads. A identificação apresentada é uma estimativa baseada nas portas que responderam; filtragem de rede pode ocultar dispositivos ou serviços. Resultados aparecem para revisão e não são inseridos automaticamente no inventário. A descoberta não consulta SNMP, endereço MAC ou fabricante. A aplicação não coleta tráfego e não coleta uso de CPU/memória. Para inventariar switches e outros equipamentos com maior confiança, será necessária integração autorizada (por exemplo SNMPv3 ou API do fabricante), com credenciais geridas como segredos, permissões somente de leitura e escopo de rede controlado.

## Segurança e privacidade

- O cadastro aceita apenas IPv4 privado/loopback ou URLs HTTP(S) destinadas a localhost/IP privado; URLs com credenciais embutidas, domínios públicos e protocolos diferentes são recusados. Isso reduz SSRF, mas não substitui isolamento de rede, ACLs nem uma whitelist adequada ao ambiente.
- O cliente HTTP não segue redirects. Não cadastre alvos fora das redes sob sua administração. Restrinja a saída de rede do host aos segmentos monitorados.
- A descoberta não é executada em segundo plano: cada operador informa a faixa e confirma a autorização. Somente IPv4 RFC1918 `/24` até `/32` é aceito e uma verificação de cada vez pode ocorrer. Restrinja ainda o host a segmentos administrados; uma rede privada pode incluir equipamentos de outras pessoas ou organizações.
- Em produção, a aplicação recusa iniciar sem a configuração `Security__TrustedAccessProxy=true`. Use-a **somente** quando houver um proxy de acesso configurado com autenticação, autorização, TLS e terminação de sessão; mantenha a porta Kestrel inacessível aos usuários e exponha a aplicação somente pela rede do proxy. A API ainda não implementa identidade ou autenticação próprias: essa proteção depende da configuração e do firewall do proxy.
- A configuração padrão é apropriada para desenvolvimento local (`127.0.0.1`). Não publique a API diretamente na internet nem use `0.0.0.0` sem uma camada de acesso segura.
- HTTPS protege o conteúdo de uma conexão HTTP(S) entre os endpoints que negociam TLS. Ele **não esconde os IPs de origem e destino necessários ao roteamento**, e sondagens ICMP não têm criptografia TLS. Em redes não confiáveis, use uma VPN/túnel entre os pontos autorizados e proteja também os endpoints; os endereços continuam visíveis aos próprios endpoints e a observadores do túnel.
- O SQLite guarda inventário e incidentes em arquivo **sem criptografia própria**. Proteja o disco e as permissões do diretório com controles do sistema operacional, backups protegidos e retenção definida. A API aplica `Cache-Control: no-store` aos endpoints de dados e cabeçalhos de segurança ao conteúdo servido.
- A aplicação evita registrar o endereço do alvo nas mensagens de falha de sondagem. Ainda assim, controle acesso, retenção e acesso administrativo aos logs do host, proxy e sistema operacional.
- No contexto da LGPD, endereços IP e identificadores de ativos podem constituir dados pessoais quando relacionados a uma pessoa natural. Defina finalidade e base legal aplicável com o controlador/encarregado, minimize os dados coletados, estabeleça retenção, transparência e controle de acesso e mantenha procedimentos para incidentes. O produto não inspeciona payloads de rede nem deve ser usado para coletar tráfego pessoal. Estas orientações técnicas não substituem avaliação jurídica.

## Disponibilidade

A disponibilidade é calculada pelo tempo acumulado em estados `online` ou `degraded`, dividido pelo tempo efetivamente observado. Períodos de triagem pendente e períodos em que a aplicação esteve parada são excluídos, pois não há sondagem nesses intervalos. Em bancos existentes, o histórico começa após a atualização; métricas anteriores não podem ser reconstruídas a partir do status atual.

## Arquitetura

- `CityPulse.Api/Program.cs`: composição de serviços, middleware e configuração do ambiente.
- `CityPulse.Api/Endpoints`: rotas de inventário e operações separadas.
- `CityPulse.Api/Security/TargetPolicy.cs`: política compartilhada de alvos para cadastro e sondagem.
- `CityPulse.Api/Services/MonitoringService.cs`: sondagens ICMP/HTTP, máquina de estados, triagem e polling em background.
- `CityPulse.Api/Data/CityPulseDbContext.cs` e `DatabaseInitializer.cs`: persistência e atualização do banco existente.
- `CityPulse.Api/Models/Entities.cs`: ativos, incidentes e contratos de API.
- `CityPulse.Api/wwwroot`: interface estática responsiva, servida pela própria aplicação.

## Próximas capacidades para uso empresarial

A base atual fornece monitoramento e inventário numa instância compartilhada, mas ainda não é uma suíte multi-organização. A evolução com maior retorno é: identidade individual via OIDC/SSO e perfis de acesso; trilha de auditoria por operador; PostgreSQL para maior concorrência/escala; notificações configuráveis por e-mail/webhook; e coletores autorizados com SNMPv3 para switches. A descoberta e integrações de rede devem ser limitadas às redes que a organização administra.
