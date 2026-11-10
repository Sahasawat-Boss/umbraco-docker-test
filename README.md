# UmbracoDocker

Umbraco 17 (LTS, .NET 10) รันใน Docker ด้วย SQLite

## สิ่งที่ต้องมีก่อน

เครื่องนี้ **ยังไม่มี Docker** — ติดตั้ง Docker Desktop ก่อน (ต้องใช้สิทธิ์ admin และรีสตาร์ตเครื่อง)

```
winget install -e --id Docker.DockerDesktop
```

หรือโหลดจาก https://www.docker.com/products/docker-desktop/

หลังติดตั้งเสร็จ เปิด Docker Desktop แล้วรอจนสถานะเป็น Running แล้วเช็คด้วย `docker info`

## ตั้งค่าหลัง clone

repo นี้ไม่ได้ commit ไฟล์ที่มีความลับ ต้องสร้างเองสองไฟล์

**1. `.env`** — copy จาก [`env.sample`](env.sample) แล้วใส่ค่าจริง

**2. `UmbracoDocker/appsettings.Development.json`** — ไฟล์นี้เก็บรหัส admin เป็น plain text เลยไม่ถูก commit

```json
{
  "$schema": "appsettings-schema.json",
  "Serilog": { "MinimumLevel": { "Default": "Information" } },
  "Umbraco": {
    "CMS": {
      "Hosting": { "Debug": true },
      "Unattended": {
        "InstallUnattended": true,
        "UnattendedUserName": "Admin",
        "UnattendedUserEmail": "you@example.com",
        "UnattendedUserPassword": "เปลี่ยนก่อนบูตครั้งแรก"
      }
    }
  },
  "ConnectionStrings": {
    "umbracoDbDSN": "Data Source=|DataDirectory|/Umbraco.sqlite.db;Foreign Keys=True;Pooling=True",
    "umbracoDbDSN_ProviderName": "Microsoft.Data.Sqlite"
  }
}
```

ถ้าไม่สร้างไฟล์นี้ Umbraco จะเปิดหน้า install wizard ให้กรอกเองตอนเข้าครั้งแรก ซึ่งก็ใช้งานได้เหมือนกัน
ตอนรันใน Docker ตัว connection string จะถูก override ด้วย env var ใน `docker-compose.yml` อยู่แล้ว

## รัน

```bash
cd C:/Users/Sahasawat_R/source/umbraco-docker
docker compose up --build
```

ครั้งแรกจะนานหน่อย (ดึง image .NET 10 sdk/aspnet + restore NuGet)

- เว็บ: https://localhost:44372 (self-signed cert) หรือ http://localhost:8080
- Backoffice: https://localhost:44372/umbraco
- ล็อกอิน: อีเมลและรหัสที่ตั้งไว้ใน `UmbracoDocker/appsettings.Development.json` (unattended install) — ดูหัวข้อ "ตั้งค่าหลัง clone"

### ให้เครื่องเชื่อใจ cert (ไม่ต้องกดผ่านหน้าเตือน)

หลังรันครั้งแรกแล้ว cert จะถูกสร้างไว้ใน `./certs` — จากนั้นรัน (PowerShell as admin):

```
./trust-cert.ps1
```

## หยุด / ล้าง

```bash
docker compose down          # หยุด แต่เก็บข้อมูล
docker compose down -v       # ลบ volume ด้วย = ล้าง DB กลับไปเริ่มใหม่
```

## โครงสร้าง

| ไฟล์ | หน้าที่ |
|---|---|
| `docker-compose.yml` | service เดียว ใช้ SQLite |
| `UmbracoDocker/Dockerfile` | multi-stage build (sdk → aspnet 10.0) |
| `UmbracoDocker/entrypoint.sh` | fix สิทธิ์ไฟล์ของ bind mount + สร้าง self-signed cert แล้ว drop สิทธิ์จาก root |
| `.env` | `CERT_PASSWORD`, `HMAC_SECRET_KEY` (และ `DB_PASSWORD` ที่ไม่ได้ใช้แล้ว) |

### ข้อมูลเก็บที่ไหน

- DB SQLite + ข้อมูล Umbraco → docker volume `umb_data` (`/app/umbraco`)
- Log → volume `umb_logs`
- `Views/`, `wwwroot/css`, `wwwroot/scripts`, `wwwroot/media`, `umbraco/models` → **bind mount** กลับมาที่โฟลเดอร์โปรเจกต์ แก้ในเครื่องแล้วเห็นผลในคอนเทนเนอร์เลย

## รันแบบไม่ใช้ Docker (ถ้าอยากลองก่อนติดตั้ง Docker)

.NET 10 SDK มีในเครื่องอยู่แล้ว:

```bash
dotnet run --project UmbracoDocker/UmbracoDocker.csproj
```

## หมายเหตุ

- โฟลเดอร์ `Database/` เป็นของ template ตอน SQL Server — ตอนนี้ไม่ได้ใช้แล้ว ลบทิ้งได้
- ถ้าอยากย้ายไป SQL Server ให้เพิ่ม service `mssql` กลับเข้ามา แล้วเปลี่ยน `ConnectionStrings__umbracoDbDSN` + `_ProviderName` เป็น `Microsoft.Data.SqlClient`
- SQLite เหมาะกับ dev/ลองเล่น ไม่แนะนำสำหรับ production ที่มีทราฟฟิกเขียนเยอะ
