
## Entrega para o Ideathon

O painel agora destaca o **Indicador 10.04 da ISO 37120** e informa a evidência usada na apuração: checks executados, incidentes observados, tempo total de indisponibilidade e média por incidente. O endpoint `GET /api/indicator` pode ser usado para exportação ou integração futura com um relatório da SEADM. A disponibilidade deixou de ser um valor demonstrativo: cada ciclo persiste `TotalChecks`, `SuccessfulChecks` e `UptimePercent` por ativo.

A demonstração recomendada é: cadastrar ou selecionar um ativo, clicar em **Forçar falha / iniciar teste**, mostrar `AGUARDANDO TRIAGEM`, responder **Sem energia local** para provar que não há cascata ou responder **Falha de equipamento / acionar SLA** para provar a cascata. Em seguida, mostrar o indicador e explicar a fórmula: `tempo total de indisponibilidade / número de incidentes`.
