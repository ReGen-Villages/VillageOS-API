# Stomata

**Works out a study's climate, as a pipeline step, from its site's hourly history.**

A pipeline hands Stomata a SiteStudy on its `subject` input. Stomata finds the study's site along the predicate the
`studiedSite` role binds, and reads the site's hourly history of six series: `temperatureCelsius`,
`relativeHumidityPercent`, `windSpeedMetresPerSecond`, `windDirectionDegrees`,
`particulateMatterTenMicrogramsPerCubicMetre` and `particulateMatterTwoAndAHalfMicrogramsPerCubicMetre`. Where one
instant holds more than one reading, the last one received is used.

For each hour it writes three readings onto the site:

| Reading | What it holds | Left out for an hour |
| --- | --- | --- |
| `windRoseCell` | the WindRoseCell whose sector holds the wind's direction and whose speed class holds its speed. The sectors, classes and cells are read from the model through the `windDirectionSector`, `windSpeedClass` and `windRoseCell` roles, so their bounds are the model's | with no direction or no speed, and with still air: the provider gives still air a direction that is no measurement, so the rose's shares are taken over the hours that have a cell |
| `humidityRatioGramsPerKilogram` | grams of water a kilogram of dry air, by ASHRAE Handbook, Fundamentals (2017), chapter 1: the Hyland-Wexler saturation pressure, and the standard atmosphere's pressure at the site's `climateDataElevationMetres` | with no temperature or no humidity, and for every hour while the elevation is unknown |
| `windowsOpenHours` | 1 when PM10 and PM2.5 are within the study's `whoDailyLimitParticulateMatterTenMicrogramsPerCubicMetre` and `whoDailyLimitParticulateMatterTwoAndAHalfMicrogramsPerCubicMetre`, else 0 | with either particulate reading missing |

The readings go through the bulk route, which sets off no recompute for each reading. Stomata then asks the
platform once to judge the site's history again, and last writes `climateWorkedOutAt` onto the study. It writes
that stamp on every run that reaches the end, a study with no site or no history included, so the study leaves the
range that started it.

A model it cannot reach, or one that cannot take the readings now, is a failure the orchestrator may try again. A
subject naming no study, a study the model does not hold, or readings the model refuses fail the call. Neither
writes the stamp, so the study stays in the range.

Mycelium launches it on the port its service Thing names, the first time a pipeline calls the connection that
reaches it. The deploy's build of the services checkout builds it with the rest of the solution.
