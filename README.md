# CityPulse NOC

Sistema de monitoramento inteligente de infraestrutura municipal para o Ideathon, agora com **backend real em C#/.NET 8**. A interface NOC é servida pelo próprio ASP.NET Core e o motor executa sondagens ICMP e HTTP reais.

## Executar

Requer o SDK .NET 8:

```bash
cd CityPulse.Api
dotnet restore
dotnet run --urls http://0.0.0.0:4173
# abrir http://localhost:4173
```

O banco SQLite `citypulse.db` é criado automaticamente. A documentação interativa fica em `/swagger`. Para disparar uma varredura fora do ciclo automático de 30 segundos, use `POST /api/poll`.

## Arquitetura real

`CityPulse.Api/Services/MonitoringService.cs` usa `System.Net.NetworkInformation.Ping` e `HttpClient` com `Parallel.ForEachAsync`, limite de concorrência 8, timeout e máquina de estados com três falhas consecutivas. `CityPulseDbContext` usa Entity Framework Core com SQLite. `PollingWorker` roda como serviço hospedado e pode ser empacotado como Windows Service ou daemon Linux.

O cadastro rejeita SSRF por padrão: aceita IPv4 privado/loopback e domínios `*.gov.br` ou `localhost`. Em produção, configure a whitelist com as sub-redes municipais autorizadas e coloque a API atrás de HTTPS, VPN e autenticação.

## Interface

`CityPulse.Api/wwwroot` contém o NOC responsivo em dark mode, com inventário, CRUD, estados, incidentes, métricas, dependências e manifesto de rotas. O painel consome `/api/snapshot`, portanto apresenta os resultados reais do motor.
