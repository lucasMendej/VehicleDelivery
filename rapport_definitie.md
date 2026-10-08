# Rapportdefinitie Afleverplanning

Dit document beschrijft de actuele afleveringslijst zoals die in `..\Reporting` is gedefinieerd. Het beschrijft zowel de rapportweergave als de bekende gegevensbron. De volledige productie-import uit KISAM/Autoline is nog niet geïmplementeerd; een deel van de rapportage draait momenteel op demo- en cachedata.

## 1. Doel en gegevensroute

Het rapport is een operationele afleverplanning voor voertuigdossiers. De beoogde gegevensroute is:

```text
KISAM/Autoline via ODBC
    -> vaste eenvoudige selectie
    -> lokale voertuigcache
    -> lokale werkplaats-/specificatiecache
    -> lokale rapportquery
    -> filters, sortering en afleverlijst
```

De gebruikerspagina heet **Afleverplanning** en de tabel heet **Afleverplanning Werkplaats**.

## 2. Bekende ODBC-selectie

In `VehicleFullImporter::sourceVehicleSelectionSql()` staat momenteel deze selectie:

```sql
SELECT TOP 100 vehicle, DESC_ AS model, REGN AS license_plate,
exec001 AS salesperson, SELLLOCN AS sales_location,
BUYLOCN AS purchase_location, NAME_ AS customer_name,
INAME AS invoice_name, RECDATEE AS expected_receipt_date,
RECDATE AS actual_receipt_date, DELDATEE AS delivery_date,
'N' AS vehicle_type
FROM vs_00_newvehiclestock
```

De selectie gebruikt bewust geen ODBC-parameters en geen `?`-placeholders. KISAM wordt benaderd met vaste queryteksten. Eventuele selectie op mutaties gebeurt via bronlogtabellen en joins in de vaste sync-query, niet via parameterbinding.

Deze query is volgens de code en documentatie uitsluitend een serverprobe voor connectiviteit en kolomnamen. Het is nog niet de definitieve productiequery voor de volledige afleverlijst.

De probe is bewust beperkt tot:

- tabel `vs_00_newvehiclestock`;
- eenvoudige kolomselectie;
- vaste querytekst zonder parameterbinding;
- maximaal 100 records.

De rapportagedocumentatie schrijft voor om geen CTE's, complexe joins, vrije SQL of database-specifieke functies naar KISAM/Autoline te sturen. Gebruikersfilters mogen niet rechtstreeks in deze bronquery worden samengevoegd.

## 3. Huidige importstatus

De huidige `import()` gebruikt nog:

```php
DB::table('demo_delivery_plans')->get();
```

De actuele import is daarmee een mock. In hoofdlijnen wordt de demo-informatie:

1. gelezen;
2. gebruikt om `reporting_vehicle_cache` te vullen;
3. gebruikt om `reporting_vehicle_specification_cache` te vullen;
4. afgesloten met een synchronisatiestatus `ready`.

De ODBC-probe is dus nog niet de import die de lokale cache voedt. Nog vast te stellen zijn onder andere de volledige productiequery, de status- en merkkolommen, het echte kommissienummer en de aparte selectie van werkplaatsregels.

## 4. Lokale voertuigcache

De rapportage gebruikt uiteindelijk `reporting_vehicle_cache`. De functionele velden zijn:

| Cacheveld | Betekenis |
|---|---|
| `vehicle` | VSB-/voertuignummer |
| `vehicle_type` | Voertuigtype, in de huidige cache meestal `N` of `U` |
| `model` | Modelomschrijving |
| `license_plate` | Kenteken |
| `brand` | Merk |
| `salesperson` | Verkoper |
| `sales_location` | Verkooplocatie |
| `purchase_location` | Voorbereidingslocatie, in het rapport `prep_location` |
| `customer_name` | Klantnaam |
| `expected_receipt_date` | Verwachte ontvangst |
| `actual_receipt_date` | Werkelijke ontvangst |
| `delivery_date` | Afleverdatum |
| `delivery_status` | Aflever-/ontvangststatus |
| `is_deleted` | Soft-delete; alleen `is_deleted = 0` wordt getoond |

Synchronisatievelden zoals `source_updated_at`, `cached_at` en timestamps zijn technisch en geen primaire gebruikerskolommen.

## 5. Werkplaatsgegevens

Werkplaatsregels staan in `reporting_vehicle_specification_cache`. Per regel kunnen onder meer omschrijving, bedrijf, ordernummer en position line worden opgeslagen.

Voor de tabel worden de regels per voertuig samengevat tot:

- totaal aantal werkplaatsregels;
- aantal regels met position line;
- aantal regels met ordernummer;
- een voortgangswaarde zoals `6-6-6`;
- een samengestelde tekst voor toelichting of tooltip.

In de actuele rapportdefinitie heten deze uitkomsten `wp_regels` en `wp_titel`.

## 6. Rapportselectie

De huidige rapportquery leest uit de lokale cache en koppelt werkplaatsregels:

```sql
from reporting_vehicle_cache v
left join reporting_vehicle_specification_cache s
  on s.vehicle = v.vehicle
 and s.vehicle_type = v.vehicle_type
where ...
  and v.is_deleted = 0
group by voertuigvelden
order by coalesce(v.delivery_date, '9999-12-31') asc,
         v.expected_receipt_date asc,
         v.vehicle asc
```

De belangrijkste geselecteerde velden zijn:

| Rapportveld | Herkomst/betekenis |
|---|---|
| `vsb_number` | `v.vehicle` |
| `commission_number` | Momenteel voorlopig gelijk aan `v.vehicle` |
| `license_plate` | `v.license_plate`; leeg wordt `Nog niet bekend` |
| `brand` | `v.brand` |
| `vehicle_type` | `v.vehicle_type` |
| `model` | `v.model` |
| `salesperson` | `v.salesperson` |
| `sales_location` | `v.sales_location` |
| `prep_location` | `v.purchase_location` |
| `customer_name` | `v.customer_name` |
| `expected_arrival` | `v.expected_receipt_date` |
| `actual_arrival` | `v.actual_receipt_date` |
| `delivery_date` | `v.delivery_date` |
| `delivery_status` | `v.delivery_status` |
| `wp_regels` | Samenvatting van gekoppelde werkplaatsregels |
| `wp_titel` | Detailtekst van gekoppelde werkplaatsregels |

De `group by` is nodig omdat meerdere werkplaatsregels per voertuig worden samengevoegd. De samengestelde velden gebruiken lokale databasefuncties zoals `count`, `sum`, `coalesce` en `group_concat`; deze query is dus niet bedoeld voor rechtstreekse uitvoering op KISAM.

## 7. Filters en selectielijsten

Alle filters werken op de lokale cache.

### 7.1 Keuzelijsten

| Parameter | Titel | Type | Waarden |
|---|---|---|---|
| `delivery_status` | Ontvangst status | Single select | Unieke `delivery_status`-waarden |
| `brand` | Merk | Single select | Unieke `brand`-waarden |
| `vehicle_type` | Nieuw of gebruikt | Single select | Unieke `vehicle_type`-waarden |
| `sales_location` | Vestiging | Single select | Unieke `sales_location`-waarden |
| `prep_location` | P Loc | Single select | Unieke `purchase_location`-waarden |

Elke keuzelijst heeft standaard een lege waarde, waarmee het filter niet actief is.

### 7.2 Tekstfilters

| Parameter | Titel | Zoekveld | Gedrag |
|---|---|---|---|
| `salesperson` | Filter verkoper | `salesperson` | Tekstzoeking met `LIKE` |
| `customer` | Filter klant | `customer_name` | Tekstzoeking met `LIKE` |

Deze tekstfilters hebben in de huidige rapportdefinitie een debounce van 200 ms.

Functioneel is de filtervoorwaarde:

```sql
where (:delivery_status = '' or v.delivery_status = :delivery_status)
  and (:brand = '' or v.brand = :brand)
  and (:sales_location = '' or v.sales_location = :sales_location)
  and (:prep_location = '' or v.purchase_location = :prep_location)
  and (:vehicle_type = '' or v.vehicle_type = :vehicle_type)
  and (:salesperson = '' or v.salesperson like :salesperson_like)
  and (:customer = '' or v.customer_name like :customer_like)
```

De runtime bouwt de waarden voor `:salesperson_like` en `:customer_like`. De gebruikersinvoer hoort als parameter te worden doorgegeven, niet als rechtstreeks samengevoegde SQL.

## 8. Tabelkolommen

De oorspronkelijke rapportdefinitie bevatte de volgende kolommen:

| Veld | Titel | Formaat/opmerking |
|---|---|---|
| `vsb_number` | VSB | Tekst |
| `commission_number` | KOMM.NR | Tekst |
| `license_plate` | KENTEKEN | Tekst |
| `brand` | MERK | Tekst |
| `vehicle_type` | TYPE | Nieuw of gebruikt |
| `model` | MODEL | Tekst |
| `salesperson` | VERK | Tekst |
| `sales_location` | V LOC | Tekst |
| `prep_location` | P LOC | Tekst |
| `customer_name` | KLANT | Tekst |
| `expected_arrival` | VERWACHT | Datum |
| `actual_arrival` | WERKELIJK ONTVANGST | Datum |
| `transport_from` | TRANSP VAN | Datum; later verwijderd |
| `transport_until` | TRANSP MAX | Datum; later verwijderd |
| `workshop_progress` / `wp_regels` | WP REGELS | Voortgang |
| `workshop_ready_at` | WP GEREED | Datum; bronafhankelijk |
| `delivery_date` | AFLEVER | Datum |
| `delivery_status` | STATUS | Badge |
| `order_notes` | ORDERTEKST | Tekst; bronafhankelijk |

Een latere migratie verwijdert `TRANSP VAN` en `TRANSP MAX`. De actuele cachequery levert deze velden niet meer.

De demo bevat `workshop_ready_at` en `order_notes`, maar deze velden worden nog niet door de actuele ODBC-probe geleverd. Ze moeten voor een productieversie nog aan een bronselectie worden gekoppeld.

## 9. Statuswaarden en badgekleuren

De demo gebruikt deze statuswaarden:

- `Binnen`;
- `Verw in 2 weken`;
- `Rest pipeline`;
- `Afleverklaar`;
- `Afgeleverd`.

De statusweergave is in de definitie als volgt bedoeld:

| Status | Visuele variant |
|---|---|
| `Binnen` | Info |
| `Verw in 2 weken` | Warning |
| `Rest pipeline` | Muted |
| `Afleverklaar` | Success |
| `Afgeleverd` | Neutral |

De definitieve bronwaarden uit KISAM moeten nog worden bevestigd.

## 10. Sortering

De actuele cachequery sorteert standaard op:

1. `delivery_date` oplopend;
2. `expected_receipt_date` oplopend;
3. `vehicle` oplopend.

Een lege afleverdatum wordt behandeld als `9999-12-31` en komt daardoor onderaan terecht.

De oorspronkelijke demoquery had daarnaast een statusprioriteit. Die prioriteit staat niet meer in de actuele cachequery; de actuele definitie gebruikt de datum- en voertuigsorteervolgorde hierboven.

## 11. KPI's

De rapportpagina bevatte oorspronkelijk deze KPI's:

- **Open dossiers**: aantal records na toepassing van de filters;
- **Te laat binnen**: records zonder werkelijke ontvangst waarvan de verwachte ontvangst vóór vandaag ligt;
- **Aflevering 7 dagen**: records met een afleverdatum vanaf vandaag tot en met zeven dagen vooruit;
- **Afleverklaar**: aantal records met status `Afleverklaar` in de onderliggende KPI-dataset.

Welke KPI's zichtbaar zijn, wordt bepaald door de gepubliceerde layoutversie. De berekeningen worden contextueel opnieuw uitgevoerd wanneer filters wijzigen.

## 12. Onbekende of nog niet definitieve onderdelen

Op basis van de huidige broncode zijn deze zaken nog niet definitief:

1. De volledige KISAM-selectie voor alle tabelkolommen.
2. De bronkolom voor `brand`.
3. De bronkolom en codering voor `delivery_status`.
4. Het echte kommissienummer; voorlopig wordt `vehicle` hergebruikt.
5. De aparte selectie van werkplaats-/specificatieregels.
6. De bron voor `order_notes` en `workshop_ready_at`.
7. De definitieve betekenis van `vehicle_type` voor KISAM.
8. De exacte wildcard- en hoofdlettergevoeligheid van de tekstfilters.
9. De synchronisatiestrategie voor nieuwe, gewijzigde en verwijderde records.

## 13. Consequentie voor de nieuwe webapp

De nieuwe applicatie kan deze rapportdefinitie functioneel volgen met:

1. een vaste eenvoudige ODBC-selectie voor KISAM;
2. een lokale tabel voor voertuigen;
3. eventueel een tweede lokale tabel voor werkplaatsregels;
4. lokale filters en sortering;
5. lokale opslag van vinkjes en aanvullende gebruikersnotities.

De KISAM-query moet klein en stabiel blijven. De rapportlogica, filtermogelijkheden, sortering en nieuwe gebruikersinvoer horen in de lokale database en de webapplicatie te worden afgehandeld.
