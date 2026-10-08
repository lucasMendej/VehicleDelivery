# Afleverlijst

ASP.NET Core Razor Pages-app voor de afleverplanning. De applicatie haalt gegevens uit KISAM/Autoline via ODBC, bewaart de ruwe brondata lokaal in SQLite en leest de rapportage uit SQLite.

## Stack en architectuur

- ASP.NET Core Razor Pages en C#.
- IIS op Windows met Windows Authentication via de bestaande site.
- KISAM/Autoline via `System.Data.Odbc`.
- SQLite als lokale import- en rapportagedatabase.
- Dapper en SQL Server zijn als technische seam aanwezig, maar worden voor de huidige rapportage niet gebruikt.

Datastroom:

```text
ODBC-bron -> ruwe JSON/SQLite source_* -> lokale verwerking -> delivery_records -> webpagina
```

De webpagina leest dus niet rechtstreeks uit ODBC.

## Bronsets

De URL accepteert `_set=1` of `_set=3`. Zonder parameter wordt set 1 gebruikt.

| URL-parameter | Bron | DSN | SQLite-set |
|---|---|---|---|
| `_set=1` | Hedin 1M | `AUTOLINE-SA` | `set_id = 1` |
| `_set=3` | Hedin 3F | `AUTOLINE-3F` | `set_id = 3` |

Set 1 en set 3 gebruiken dezelfde selectiecatalogus. Alleen de DSN, brondata, lokale `source_*`-tabellen, `set_id` en configureerbare bronmapping verschillen.

Belangrijke pagina’s:

- `/afleverlijst?_set=1` — rapportage set 1.
- `/afleverlijst?_set=3` — rapportage set 3.
- `/afleverlijst/load?_set=1` — loaddiagnostiek set 1.
- `/afleverlijst/load?_set=3` — loaddiagnostiek set 3.

## Load en synchronisatie

Bij de eerste opening van een set wordt automatisch een initial load gestart:

1. `Configuration/delivery-selections.json` wordt gelezen.
2. De bronconfiguratie voor de gekozen set wordt geselecteerd.
3. Elke query wordt zonder ODBC-parameters uitgevoerd, omdat KISAM dit niet betrouwbaar ondersteunt.
4. Ruwe rijen worden als JSON opgeslagen in `data/<set>/<mode>/`.
5. De rijen worden ook opgeslagen in SQLite `source_*`-tabellen.
6. `SqliteDataStore.ProcessSetAsync(_set)` verwerkt de brondata naar `delivery_records`.
7. De webpagina leest de bijgewerkte rapportage uit SQLite.

Dezelfde keten wordt gebruikt voor sync. Sync gebruikt de `VS_00_RECOVERYAUDITLOG`-filter uit de selectieconfiguratie. Er wordt geen `changes.log`-selectie uitgevoerd.

De automatische sync draait standaard elke tien minuten zolang er actieve gebruikers zijn. De browser stuurt iedere tien seconden een heartbeat. De loadpagina controleert de status iedere vijf seconden. Een volledige browserrefresh gebeurt alleen bij afronding van een load.

## Selecties

De queries staan los van de programmacode in:

```text
Configuration/delivery-selections.json
```

De groepen zijn `initial` (voertuigen, hulptabellen en specificaties) en `sync` (gewijzigde voertuigen en specificaties).

De voertuigselecties voor nieuw en gebruikt worden na ophalen lokaal samengevoegd. Er wordt geen `UNION` op KISAM uitgevoerd.

Specificaties leveren momenteel:

```text
MAGIC, VTYPE, desc002, socomp, poshdr, orderno, coststat
```

De selectiecatalogus is niet set-specifiek. Bronverschillen worden opgelost met placeholders en mapping in `appsettings.json`. De placeholder `{DELIVERY_DATE}` wordt vóór uitvoering vervangen door `DELDATE` of `DELDATEE`.

De loader normaliseert de gekozen waarde daarna naar het lokale veld `DELIVERY_DATE`. Hierdoor gebruikt de rapportage één intern veld, onafhankelijk van de bronkolom.

## SQLite-opbouw

Ruwe tabellen zijn per set en modus gescheiden, bijvoorbeeld:

```text
source_1_vehicles_initial
source_3_vehicles_initial
source_1_vehicles_sync
source_3_vehicles_sync
source_3_support_locations_initial
source_3_support_salespersons_initial
source_3_vehicle_specifications_initial
```

De opslaglaag voegt `_initial` of `_sync` toe aan hulptabellen wanneer `outputName` die suffix nog niet bevat. Dit voorkomt dat hulptabellen buiten de verwerking vallen.

De rapportagetabel is `delivery_records`. De upsert-sleutel is `(set_id, vsb_number, vehicle_type)`. Oude voertuigen worden niet verwijderd; historische regels blijven beschikbaar.

De rapportagetabel bevat onder andere verkoper- en locatieomschrijvingen, klantnaam, factuurnaam, werkplaatstellingen, WP-tips, unieke locatie-werkordercombinaties, `selected`, `polished` en `user_note`.

De aparte tabel `delivery_record_fields` bevat `activestate_1`, `activestate_2`, `text`, `_set`, `vtype` en `vehicle`.

Scripts:

- `database/afleverlijst_init.sql` — tabellen en indexen.
- `database/afleverlijst_report.sql` — handmatige rapportagetemplate met `__SET_ID__`.

De webapp gebruikt voor automatische verwerking de C#-implementatie in `Data/SqliteDataStore.cs`; de SQL-bestanden zijn voor controle, beheer en herstel.

## Hulptabellen en koppelingen

- `support.salespersons`: `userid` naar `name_`.
- `support.locations`: `code` naar `name_`.
- `support.brands`: bron-steuntabel voor merken.

Locatienamen worden direct na ODBC opgeschoond. De teksten `Hedin `, `Automotive `, `1M ` en `3F ` worden verwijderd. Dit gebeurt ook bij verwerking en uitlezen van bestaande SQLite-records.

De gewone locatiekolommen tonen de omschrijving uit `support.locations`. De filter **W-regels** gebruikt `socomp` uit WP-specificaties, koppelt die aan `support.locations.code` en toont dezelfde omschrijving.

De voertuigselecties leveren `LETTER` mee. De rapportage-opbouw koppelt deze code aan `support.brands.LETTER` en toont de bijbehorende `DESC002` als merk. De merktabel wordt daarnaast rechtstreeks gebruikt voor de opties van het merkfilter.

Locatienamen worden na ODBC opgeschoond door `Hedin `, `Automotive `, `1M ` en `3F ` te verwijderen.

## Werkplaatsregels en status

Per voertuig worden specificatieregels gegroepeerd op `VTYPE` en `MAGIC`. De teller is:

```text
totaal - poshdr <> 0 - orderno <> 0
```

De hovertekst bevat per regel:

```text
desc002 locatie socomp werkorder orderno - coststat
```

Bij `coststat = R` staat een vinkje voor de regel.

Statusvolgorde:

1. alle WP-regels hebben `coststat = R` → `Gefactureerd`;
2. `orderno` gevuld → `Aangemaakt`;
3. `poshdr` gevuld → `Gepushed`;
4. `RECDATE` gevuld → `Binnen`;
5. `RECDATE` leeg en verwachte datum meer dan 14 dagen vooruit → `Verwacht in 2 weken`;
6. anders met verwachte datum gevuld → `Binnen 2 weken`.

Initial- en sync-specificaties kunnen dezelfde regels bevatten. Deze worden lokaal gededupliceerd zodat WP-regels niet dubbel worden geteld.

In de kolom Ontvangst heeft `RECDATE` voorrang. Als die leeg is, wordt `RECDATEE` getoond met het label **Verwachte datum**.

## Gereed en opslaan

Checkboxstatus wordt opgeslagen in `delivery_records.selected`. De poetsstatus wordt opgeslagen in `delivery_records.polished`. Het formulier stuurt alle zichtbare regels mee. Daardoor kunnen beide vinkjes ook weer worden uitgezet zonder dat regels buiten een filter worden gewijzigd.

Wijzigingen worden gelogd in `delivery_ready_log` en `delivery_polished_log`, inclusief gebruiker, tijdstip, voertuig en nieuwe waarde. De tooltip van beide vinkjes toont de gebruiker zonder het prefix `HEDIN\\` en de tijd als `dd-MMTHH:mm`.

Filter-, sorteer- en `_set`-waarden blijven behouden na opslaan en na **Wis alles**. De tile **Gereed** toont het aantal aangevinkte regels binnen de actuele selectie.

## Frontend en responsive gedrag

De rapportagepagina gebruikt de samenvattingstegels ook als primaire navigatie voor filters:

- De tegel **Open dossiers** staat als laatste tegel en toont het aantal regels binnen de actuele selectie.
- Zonder actieve filters toont deze tegel de tekst `Klik hier om het juiste filter in te stellen`.
- Met actieve filters wordt een korte omschrijving getoond, bijvoorbeeld `Alle dossiers met WP-regels voor Hoofddorp`.
- Een klik op de tegel opent het bestaande filterformulier als overlay boven de tabel. Sluiten kan met de sluitknop of met `Escape`.

De tabel is bewust breder dan een klein scherm om alle kolommen leesbaar te houden. Daarom heeft de tabel een minimale inhoudsbreedte, maar wordt horizontaal scrollen afgevangen door `.table-wrap`. De tabel mag niet met `overflow: visible` buiten het witte tabelpaneel tekenen; dat veroorzaakt overlap met andere paginaonderdelen.

De locatie- en synchronisatiepills staan onder de tegels op dezelfde regel als de laadstatus. Tijdens een initial load krijgt de volledige `.dashboard-meta`-regel een gekleurde achtergrond over de breedte van het dashboard. De tekst en spinner blijven in het statusgedeelte staan, zodat de laadmelding op dezelfde plek blijft.

Bij wijzigingen aan deze layout moeten de laatste responsive regels in `wwwroot/css/site.css` behouden blijven. De basisstijl is grotendeels compact geschreven en wordt onderaan aangevuld met gerichte overrides. Controleer na frontendwijzigingen minimaal:

```powershell
dotnet build --no-restore
```

## Configuratie

De relevante configuratie staat onder `DeliverySources` in `appsettings.json`:

```json
{
  "DefaultSet": 1,
  "SyncIntervalMinutes": 10,
  "SessionTimeoutSeconds": 90,
  "DataPath": "data",
  "DatabasePath": "database/afleverlijst.sqlite",
  "UseSampleData": false,
    "Sources": {
    "1": { "Name": "autoline-sa", "Dsn": "AUTOLINE-SA", "Username": "...", "Password": "...", "FieldMapping": { "NewDeliveryDate": "DELDATEE", "UsedDeliveryDate": "DELDATE" } },
    "3": { "Name": "autoline-3f", "Dsn": "AUTOLINE-3F", "Username": "...", "Password": "...", "FieldMapping": { "NewDeliveryDate": "DELDATE", "UsedDeliveryDate": "DELDATE" } }
  }
}
```

Gebruik op productie bij voorkeur IIS environment variables voor wachtwoorden:

```text
DeliverySources__Sources__1__Password
DeliverySources__Sources__3__Password
```

Zet `UseSampleData=false` op productie. Bij `true` gebruikt de pagina de voorbeeldrepository uit `dataset/`.

## Deployment en IIS

Publiceren:

```powershell
dotnet publish -c Release -o .\publish
.\publish.ps1
```

`publish.ps1` kopieert naar `\\FRARPTNLPAPP01\inetpub\webapps\Afleverlijst`.

Het script kopieert bewust niet `database`, `dataset`, `appsettings*.json` of `*.pdb`. De server behoudt zo de lokale database en configuratie.

De IIS AppPool-identiteit heeft schrijfrechten nodig op `.keys`, `data` en eventueel `logs`. De applicatie draait als child-applicatie binnen een bestaande Windows-authenticated site; authentication-secties mogen niet opnieuw worden gedefinieerd als ze op parentniveau vergrendeld zijn.

## Diagnostiek

Controleer op `/load?_set=1` of `/load?_set=3`:

- bronnaam en DSN;
- initial- en sync-query’s;
- aantal rijen en duur per ODBC-selectie;
- JSON-paden;
- lokale SQLite-verwerking;
- foutmelding en stacktrace.

Veelvoorkomende problemen:

### `DELDATEE` niet gevonden

De bron gebruikt waarschijnlijk een andere afleverdatumkolom. Controleer `FieldMapping` in `appsettings.json`. De selecties horen `{DELIVERY_DATE}` te gebruiken; de loader vervangt dit automatisch met de geconfigureerde bronkolom.

### Verkopers of locaties tonen codes

Controleer of bijvoorbeeld `source_3_support_locations_initial` en `source_3_support_salespersons_initial` bestaan. Voer daarna **Verwerk SQLite naar rapportage** uit. Oude tabellen zonder modus-suffix worden als fallback gelezen.

### WP-regels zijn nul

Controleer of `source_<set>_vehicle_specifications_initial` bestaat en de velden `MAGIC`, `poshdr`, `orderno`, `socomp` en `coststat` bevat. Voer daarna opnieuw de lokale verwerking uit.

### Merkenfilter is leeg

Controleer bij een nieuwe bron of de voertuigselectie `LETTER` teruggeeft en of die waarde voorkomt in `support.brands.LETTER`. De rapportage gebruikt `DESC002` als zichtbare merknaam.

### Oude rapportagedata

Controleer `UseSampleData=false`, het databasepad en de logregel `Lokale verwerking afgerond`. De webpagina leest uitsluitend `delivery_records`.

## Lokale ontwikkeling

```powershell
dotnet run
```

Compileercontrole terwijl de applicatie al draait:

```powershell
dotnet build --no-restore -p:UseAppHost=false -o .\build-check
```

Gebruik `UseSampleData=true` alleen voor een lokale demo zonder ODBC.

## 17. Actuele UI-uitleg

De tabelkoppen `Klant`, `Locaties`, `Aflever`, `Status` en `WP regels` hebben een `i`-icoon met een browser-tooltip.

- `Klant`: eerste naam is de klantnaam; tweede naam is de factuurnaam.
- `Locaties`: `V` is verkooplocatie; `P` is fysieke/voorbereidingslocatie.
- `Aflever`: oranje betekent vandaag/morgen met nog niet alle regels gefactureerd; grijs betekent binnen zeven dagen met nog niet alle regels gepushed.
- `Status`: bevat de betekenis en prioriteit van alle statussen.
- `WP regels`: toont totaal, gepushed en op order.

Onder de WP-teller staat een unieke lijst `locatie-werkorder`, bijvoorbeeld `31-41698` of `11-25865, 11-24665`. Werkordernummer `0` wordt daar niet getoond.

De statuskolom is sorteerbaar. Filters, sortering en `_set` blijven behouden na opslaan. De pagina toont alleen afleverregels waarvan `delivery_date` vandaag of later is.

## 18. Release-checklist

1. Pas bronselecties aan in `Configuration/delivery-selections.json`.
2. Pas bronverschillen aan in `appsettings.json` via `FieldMapping`.
3. Controleer de JSON-configuratie.
4. Voer `dotnet build --no-restore` uit.
5. Publiceer met `dotnet publish -c Release -o .\publish`.
6. Kopieer de applicatie met `publish.ps1`.
7. Controleer dat productie-`appsettings.json`, database en configuratie niet ongewenst zijn overschreven.
8. Open `/afleverlijst/load?_set=1` en `_set=3`.
9. Voer na wijzigingen aan selecties een initial load uit.
10. Controleer de effectieve SQL en de lokale verwerking in de loadlog.

## 19. Bekende operationele aandachtspunten

- De applicatie maakt ontbrekende SQLite-kolommen automatisch aan bij het eerste rapportageverzoek.
- Nieuwe rapportagevelden worden pas gevuld na een lokale verwerking vanuit de `source_*`-tabellen.
- Bij een overgezette SQLite-database moet na code- of selectiewijzigingen **Verwerk SQLite naar rapportage** opnieuw worden uitgevoerd.
- De ODBC-driver ondersteunt geen betrouwbare parameterbinding; houd bronquery's eenvoudig en parameterloos.
- De live applicatie gebruikt de configuratie en database op de server, niet de bestanden in de lokale ontwikkelmap.
