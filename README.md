# KUKA AMR Middleware

Middleware pre moste system na správu misií KUKA AMR Fleet Managera (KUKA FM). Implementácia pokrýva dispatch a FORCE cancel misií `RACK_MOVE`, synchronizáciu robotov a aktívnych misií, stavové histórie, API aj webové rozhranie.

## Funkcie

- Dispatch `RACK_MOVE` pre dostupného robota: štartovací a cieľový uzol, kód kontajnera a priorita 1–99.
- Zrušenie aktívnej misie cez KUKA FM `missionCancel` s režimom `FORCE`.
- Polling robotov a aktívnych misií; ukončené misie sa ďalej nepollujú.
- História stavov misií a robotov vrátane intervalov platnosti.
- Filtrovanie misií podľa stavu, zobrazenie histórie a automatické obnovenie UI každú sekundu.
- PostgreSQL s EF Core migráciami, API key ochranou a štruktúrovanými JSON logmi do konzoly a Seq.

Podrobná špecifikácia a trasovanie akceptačných kritérií sú v [docs/architecture.md](docs/architecture.md).

## Lokálny štart

1. Skopíruj `.env.example` do `.env` a nastav aspoň `POSTGRES_PASSWORD`, `MIDDLEWARE_API_KEY` a `KUKA_FM_TOKEN`. Pri lokálnom vývoji je Seq dostupný bez prihlásenia iba cez loopback; v produkčnom CD sa povinne používa `SEQ_ADMIN_PASSWORD`.
2. Spusti Docker Desktop v režime Linux containers.
3. V koreňovom priečinku projektu spusti:

```powershell
docker compose up -d --build
```

4. Otvor:
   - Aplikácia: <http://localhost:8080/>
   - Health check: <http://localhost:8080/api/health>
   - Seq log viewer: <http://localhost:5341/>

Databázové migrácie sa aplikujú automaticky pri štarte API. Existujúca databáza vytvorená staršou verziou cez `EnsureCreated` sa konvertuje bez vymazania misií a robotov.

## API

Všetky aplikačné `/api/*` endpointy vyžadujú hlavičku `X-Api-Key`, okrem verejného health endpointu.

| Metóda | Cesta | Účel |
| --- | --- | --- |
| `GET` | `/api/health` | Health check pre pipeline |
| `GET` | `/api/missions?state=active\|finished` | Zoznam misií; filter je voliteľný |
| `POST` | `/api/missions` | Dispatch misie |
| `POST` | `/api/missions/{missionCode}/cancel` | FORCE cancel aktívnej misie |
| `DELETE` | `/api/missions/{missionCode}` | Spätná kompatibilita pre cancel |
| `GET` | `/api/missions/{missionCode}/history` | História stavov misie, najnovšie prvá |
| `GET` | `/api/robots` | Aktuálne stavy robotov |
| `GET` | `/api/robots/{robotId}/history` | História stavov robota, najnovšie prvá |

Príklad tela pre dispatch:

```json
{
  "robotId": "R-01",
  "startNode": "A-01",
  "targetNode": "A-02",
  "containerCode": "BIN-01",
  "priority": 50
}
```

## Testy a build

```powershell
dotnet restore KukaMiddleware.Tests/KukaMiddleware.Tests.csproj
dotnet build KukaMiddleware.Tests/KukaMiddleware.Tests.csproj --configuration Release --warnaserror
dotnet test KukaMiddleware.Tests/KukaMiddleware.Tests.csproj --configuration Release
dotnet format KukaMiddleware.Tests/KukaMiddleware.Tests.csproj --verify-no-changes --no-restore
```

## Azure DevOps CI/CD

- [azure-pipelines.yml](azure-pipelines.yml) spúšťa CI pri zmenách do `main`: restore, kontrolu zraniteľných NuGet balíkov, formátovanie/analyzéry, build, testy a Docker build.
- [ci-cd/cd.yml](ci-cd/cd.yml) je manuálny CD pipeline pre Docker Compose na self-hosted Azure agentovi s Docker Desktop (Linux containers).
- Pred použitím CD vytvor v Azure DevOps Library variable group `kuka-middleware-deploy`, povoľ jej použitie v pipeline a nastav:
  - `DOCKER_AGENT_POOL` – pool s agentom, ktorý hostuje cieľový Docker Desktop.
  - `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `POSTGRES_PORT`.
  - `MIDDLEWARE_API_KEY`, `KUKA_FM_BASE_URL`, `KUKA_FM_ORG_ID`, `KUKA_FM_TOKEN`, `KUKA_FM_ALLOW_UNTRUSTED_CERTIFICATE`, `KUKA_FM_POLL_INTERVAL_SECONDS`.
  - `SEQ_ADMIN_PASSWORD`.

Citlivé hodnoty musia byť označené ako secret variables. Pipeline ich posiela procesu ako environment variables; nevkladá ich do repozitára ani do argumentov príkazového riadka. Voliteľný `SEQ_API_KEY` sa môže nastaviť v lokálnom `.env`. CD po nasadení overí UI a `/api/health`.

## KUKA FM integrácia

KUKA `Authorization` token sa posiela bez prefixu `Bearer`; nikdy sa nezapisuje do logov. Povolenie self-signed certifikátu je vypnuté štandardne a má sa zapnúť iba pre dôveryhodný školský KUKA FM endpoint.
