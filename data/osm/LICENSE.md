# OpenStreetMap street data: ODbL 1.0

`streets.csv.gz` is a **derived database** of street names from OpenStreetMap (Geofabrik Türkiye extract; see `SOURCES.json`
for the snapshot hash).

© OpenStreetMap contributors. The data is made available under the Open Database License (ODbL) 1.0:
<https://opendatacommons.org/licenses/odbl/1-0/>. Individual contents of the database are licensed under the Database
Contents License: <https://opendatacommons.org/licenses/dbcl/1-0/>. See also <https://www.openstreetmap.org/copyright>.

What this means for you:

- **Attribution:** keep "© OpenStreetMap contributors" where you show data from this file.
- **Share-alike:** if you publicly use a database derived from this file, you must offer it under ODbL as well.
  Using the data inside an application (a "produced work") only requires attribution.
- **Separation:** this file ships only in the `AdresTR.Streets` package. The AdresTR code, `AdresTR`, `AdresTR.Data` and
  the other packages do not contain OpenStreetMap data and remain MIT / CC0.

The extraction code is `data/scripts/build_osm_streets.py` (MIT).
