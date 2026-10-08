# API e desenvolvimento

O serviço usa ASP.NET Core .NET 8, EF Core e SQLite. Consulte o [README principal](../README.md) para instalação, implantação, segurança e privacidade.

## Organização

- `Program.cs`: composição de serviços, middleware e configuração do ambiente.
- `Endpoints/AssetEndpoints.cs`: cadastro, consulta, atualização, exclusão e triagem de ativos.
- `Endpoints/OperationalEndpoints.cs`: ciclo de sondagem, health check e resumo operacional.
- `Endpoints/DiscoveryEndpoints.cs`: início explícito da descoberta de uma faixa autorizada.
- `Security/TargetPolicy.cs`: validação dos alvos de sondagem permitidos.
- `Services/MonitoringService.cs`: sondagens ICMP/HTTP, transições de estado, disponibilidade e incidentes.
- `Services/NetworkDiscoveryService.cs`: sondagem limitada de IPv4 privado, verificação de serviços TCP e classificação aproximada com evidências.
- `Data/CityPulseDbContext.cs`: mapeamentos do EF Core.
- `Data/DatabaseInitializer.cs`: criação do banco e atualização compatível do SQLite existente.
- `Models/Entities.cs`: entidades e contratos da API.
- `wwwroot/`: interface estática e recursos do app instalável.

## Build local

Na raiz do repositório:

```powershell
dotnet build .\CityPulse.Api\CityPulse.Api.csproj /nr:false
dotnet run --project .\CityPulse.Api\CityPulse.Api.csproj
```

O modo Development disponibiliza o Swagger. Não o exponha publicamente. Em Production, o serviço exige configuração explícita de um proxy confiável com autenticação e TLS; essa configuração não substitui autenticação no próprio serviço e deve ser usada somente atrás desse proxy.

## Rotas principais

| Método | Rota | Uso |
| --- | --- | --- |
| `GET` | `/api/assets` | Listar ativos |
| `GET` | `/api/assets/{id}` | Consultar um ativo |
| `POST` | `/api/assets` | Cadastrar ativo |
| `POST` | `/api/assets/discovered/bulk` | Adicionar em lote até 254 resultados revisados |
| `PUT` | `/api/assets/{id}` | Atualizar ativo |
| `DELETE` | `/api/assets/{id}` | Excluir ativo |
| `POST` | `/api/assets/{id}/status` | Iniciar ou resolver triagem |
| `POST` | `/api/poll` | Executar sondagem manual |
| `POST` | `/api/discovery/scan` | Descobrir endereços em uma faixa privada autorizada |
| `GET` | `/api/snapshot` | Ler o resumo operacional |
| `GET` | `/api/health` | Consultar estado básico do serviço |

Para concluir uma triagem, `resolution` aceita `POWER_OUTAGE`, `NETWORK_FAULT` ou `RESTORED`. A rotina rejeita alvos fora da política de endereços privados/localhost; redirects HTTP ficam desativados para impedir que o alvo redirecione a sondagem a outro destino.

`POST /api/discovery/scan` recebe `{"cidr":"192.168.1.0/24","authorized":true}`. Aceita somente IPv4 RFC1918 com prefixo `/24` até `/32`, limita cada execução a 254 endereços e impede duas varreduras simultâneas. Faz sondagens ICMP e conexões TCP breves às portas 22, 80, 443, 445, 554, 3389 e 9100. Não autentica, faz exploração, consulta SNMP nem captura tráfego. A API devolve os endereços responsivos e as evidências usadas para uma classificação aproximada; os resultados não são persistidos. Use apenas em redes que você tem autorização para verificar.

`POST /api/assets/discovered/bulk` recebe `{"authorized":true,"devices":[{"address":"192.168.1.12","category":"Computador ou servidor provável"}]}`. O painel seleciona inicialmente apenas os resultados ainda não inventariados e envia os escolhidos em uma única transação; endereços/categorias inválidos ou repetidos recusam o lote inteiro, enquanto equipamentos já presentes são ignorados e contados na resposta. O máximo por lote é 254.

## Cuidados com dados

Endereços, nomes e localizações cadastrados podem identificar pessoas ou revelar informações da infraestrutura. Restrinja a rede e o acesso ao serviço, não inclua dados pessoais desnecessários nesses campos e proteja exports CSV. O SQLite local não oferece criptografia em repouso; a organização responsável define a finalidade, a retenção, a base legal e o atendimento aos direitos aplicáveis. O produto não declara conformidade automática com a LGPD.
