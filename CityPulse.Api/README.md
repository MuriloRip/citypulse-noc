# CityPulse.Api — backend real

Backend **ASP.NET Core .NET 8** do CityPulse, com SQLite/Entity Framework Core e sondagens reais.

## O que é real

- `System.Net.NetworkInformation.Ping` para ICMP IPv4.
- `HttpClient` com timeout de 5 segundos para HTTP/HTTPS.
- `Parallel.ForEachAsync` com grau máximo de paralelismo 8.
- Máquina de estados: 3 falhas consecutivas para confirmar `Down`.
- Propagação pai-filho para `Unreachable`.
- Persistência local em `citypulse.db`.
- Worker automático a cada 30 segundos.
- Swagger em `/swagger`.

## Execução

```bash
dotnet restore
dotnet run --urls http://0.0.0.0:4173
```

Abra `http://localhost:4173`. A interface NOC fica em `wwwroot`.

## Whitelist SSRF

Por segurança, o MVP aceita apenas IPv4 privado/loopback e domínios `*.gov.br` ou `localhost`. Para a instalação municipal, substitua `TargetPolicy.IsAllowed` por uma configuração com as sub-redes oficiais de Campo Mourão e os domínios autorizados.

## API principal

- `GET /api/snapshot` — painel completo.
- `GET /api/assets` — inventário.
- `POST /api/assets` — cadastro.
- `PUT /api/assets/{id}` — edição.
- `DELETE /api/assets/{id}` — exclusão.
- `POST /api/poll` — dispara uma varredura real imediatamente.
- `GET /api/health` — health check.
