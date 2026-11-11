# Admin Portal

Responsive web application with public sign-up, password and QR code sign-in, forgotten password emails,
and administration of users and access profiles (roles).

- **Frontend:** Angular 22, Angular Material (`frontend/`)
- **Backend:** ASP.NET Core 10 Web API, EF Core (`backend/UserAdmin.Api`)
- **Database:** SQL Server (LocalDB in development)

## Run it

Prerequisites: .NET 10 SDK, Node.js LTS, SQL Server LocalDB.

```powershell
# Terminal 1: API on http://localhost:5080. Creates and migrates the database on startup.
dotnet run --project backend/UserAdmin.Api --launch-profile http

# Terminal 2: app on http://localhost:4200, forwarding /api to the API.
cd frontend
npm install
npm start
```

The **first** API start against an empty database creates the administrator `admin@example.com` and prints a
random password to the console, once. You are asked to replace it when you first sign in. Change the
administrator's email to a real address on the Users page, so that forgotten password emails can reach it.

**Signing key:** tokens are signed with `Jwt:Key`. On a new development machine, generate one. It is stored in
.NET user secrets, outside this repository:

```powershell
$bytes = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String($bytes)) --project backend/UserAdmin.Api
```

**Emails in development:** without a mail server, sign-up and password emails are printed in the API's terminal.
Copy the link from there into your browser.

If you lose the first password before signing in, delete the database and start the API again:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "ALTER DATABASE UserAdmin SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE UserAdmin"
```

## Tests

```powershell
dotnet test UserAdmin.slnx        # API tests; each run uses a temporary LocalDB database
cd frontend; npx ng test --watch=false
```

## Business modules

- **Articles:** the spare-parts catalogue. Each article has a unique reference (part number), brand, category,
  compatible vehicles, shelf location, main supplier, purchase and sale prices, quantity in stock and a minimum stock.
  An article is *low on stock* when its quantity is at or below the minimum. Search covers reference, name, brand
  and compatible vehicles. The home page shows the number of active articles, how many are low or out of stock, and
  the stock value at purchase price.
- **Suppliers** and **Customers:** name, contact person, email, phone, address, city, tax number (matricule fiscal)
  and notes. Names are unique. A supplier that articles still use cannot be deleted; deactivate it instead.
- Prices are in TND with 3 decimals. The currency label is `CURRENCY` in `frontend/src/app/core/models.ts`.
- Each module has a *view* and an *edit* permission, chosen per profile on the Profiles page. New accounts on
  `Standard user` get none of them.

Quantities are typed in directly for now; purchases and sales do not change stock yet.

## How access works

A **profile** is a named set of permissions, and each user has one profile. The `Administrator` profile is
a system profile: it always holds every permission and cannot be edited or deleted.

To add a permission for a new feature:

1. Add it to the catalog in `backend/UserAdmin.Api/Auth/Permissions.cs`. It then appears in the profile editor.
2. Protect endpoints with `[Authorize(Permissions.YourPermission)]`. Endpoints without an attribute still require a signed-in user.
3. Add it to `frontend/src/app/core/models.ts` to use it in route guards and the UI.

Rules enforced by the API:

- Permissions and account status are read from the database on every request, so changes apply immediately.
- Administrators cannot deactivate, delete or change the profile of their own account.
- Nobody can manage a user, assign a profile, or edit a profile that has permissions they do not hold themselves.
- A profile that is assigned to users cannot be deleted.
- Accounts on a temporary password can reach only their own account endpoints until they replace it.

Sessions use a 15-minute access token held in memory, plus a 7-day rotating refresh token in an `HttpOnly`,
`SameSite=Strict` cookie. Replaying an old refresh token revokes that whole login. Accounts lock for 15 minutes
after 5 failed sign-ins, and sign-in attempts are rate limited per IP address.

## Public sign-up, forgotten passwords and QR codes

- **Sign-up:** a new account has no password until its owner uses the link emailed to them, so nobody can claim an
  address they do not control. New accounts get the profile marked as the default, `Standard user`. Any permission
  added to that profile is open to the public.
- **Forgotten password:** emails a link that works once and lasts 1 hour. Using it signs the account out everywhere.
- **Sign-up and forgotten password never reveal whether an email has an account.** Each address gets at most one email a minute.
- **QR codes:** each person creates their own code on My account by confirming their password. It lasts 30 days,
  and only a hash of its secret is stored. Replacing it, turning it off, changing or resetting the password, or
  deactivating the account cancels it. Administrators cannot create one for someone else, because that would let them
  sign in as that person unnoticed.
- **A QR code is a credential.** Anyone who scans it, or a photo of it, is signed in as that account.

The code holds a link to the address the app was opened on. A phone's camera can only open it once the app is hosted
somewhere the phone can reach. On `localhost`, use **Sign in with a QR code** on the sign-in page, which reads the code
with the computer's camera or from a saved picture. Browsers only allow camera access on HTTPS or `localhost`.

## Configuration

| Setting | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings:Default` | SQL Server connection | LocalDB, database `UserAdmin` |
| `Jwt:Key` | Token signing key, at least 32 bytes. Use a different key for each environment | Random per run in Development if unset; **required** elsewhere |
| `App:BaseUrl` | Public address used in emailed links | `http://localhost:4200` in Development; **required** elsewhere |
| `Email:SmtpHost` | Mail server | Print to console in Development; **required** elsewhere |
| `Email:SmtpPort`, `Email:EnableSsl` | Mail server connection | `587`, `true` |
| `Email:Username`, `Email:Password` | Mail server login | None |
| `Email:From` | Sender address | Required with `Email:SmtpHost` |
| `Seed:AdminEmail` | First administrator's email | `admin@example.com` |
| `Seed:AdminPassword` | First administrator's password | Generated and printed |
| `RateLimiting:LoginPermitPerMinute` | Sign-in attempts per IP per minute | `10` |

## Database changes

```powershell
dotnet tool restore
dotnet ef migrations add YourChange --project backend/UserAdmin.Api --output-dir Data/Migrations
```

Migrations are applied automatically when the API starts.
