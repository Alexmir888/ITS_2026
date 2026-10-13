# KUKA AMR Middleware

Middleware pre moste system na spracovanie a riadenie misií robotov cez KUKA FM. Projekt je implementovaný ako ASP.NET Core 8 minimal API + PostgreSQL + jednoduché webové UI.

## Obsah

- [Ciele a stav](docs/architecture.md)
- [Use cases](docs/architecture.md#use-cases)
- [Dátový model](docs/architecture.md#erd)
- [View aplikácie](docs/architecture.md#wireframe-n%C3%A1vrh)
- [Technológie](docs/architecture.md#technol%C3%B3gie)

## Rýchly start

1. Uprav `.env` podľa potreby.
2. Spusti Docker Desktop.
3. V root priečinku spusti:

```powershell
docker compose up -d --build
```

4. Otestuj health endpoint:

```powershell
Invoke-RestMethod http://localhost:8080/health
```

5. Otevri webové UI:

```
http://localhost:8080/
```

## API

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Health check |
| `GET` | `/api/missions` | Zoznam misií |
| `POST` | `/api/missions` | Dispatch novej misie |
| `DELETE` | `/api/missions/{missionCode}` | Cancel misie |
| `GET` | `/api/missions/{missionCode}/history` | História stavu |
| `GET` | `/api/robots` | Zoznam robotov |

## Autorizácia

Všetky `/api/*` requesty vyžadujú hlavičku `X-Api-Key`.

## KUKA FM integrácia

Projekt posiela outbound HTTP requesty na KUKA FM s hodnotou `Authorization` rovnou tokenu, bez prefixu `Bearer`.

## Dokumentácia

Kompletný projektový dokument podľa zadania je v súbore:

- [docs/architecture.md](docs/architecture.md)

## Verifikácia

Projekt bol verifikovaný local build/testom v Docker:

- 5/5 testov prešlo
- health endpoint vracia `ok`
- UI sa serveruje na `/`
