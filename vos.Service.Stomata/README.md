# Stomata

**Works out a study's climate, as a pipeline step, from its site's hourly history.**

A pipeline hands Stomata a SiteStudy on its `subject` input. Stomata finds the study's site along the predicate the
`studiedSite` role binds, and reads the site's hourly history of six series: `temperatureCelsius`,
`relativeHumidityPercent`, `windSpeedMetresPerSecond`, `windDirectionDegrees`,
`particulateMatterTenMicrogramsPerCubicMetre` and `particulateMatterTwoAndAHalfMicrogramsPerCubicMetre`. Where one
instant holds more than one reading, the last one received is used.

For each hour it writes these readings onto the site:

| Reading | What it holds | Left out for an hour |
| --- | --- | --- |
| `windRoseCell` | the WindRoseCell whose sector holds the wind's direction and whose speed class holds its speed. The sectors, classes and cells are read from the model through the `windDirectionSector`, `windSpeedClass` and `windRoseCell` roles, so their bounds are the model's | with no direction or no speed, and with still air: the provider gives still air a direction that is no measurement, so the rose's shares are taken over the hours that have a cell |
| `humidityRatioGramsPerKilogram` | grams of water a kilogram of dry air, by ASHRAE Handbook, Fundamentals (2017), chapter 1: the Hyland-Wexler saturation pressure, and the standard atmosphere's pressure at the site's `climateDataElevationMetres` | with no temperature or no humidity, and for every hour while the elevation is unknown |
| `windowsOpenOnHourlyAirHours` | 1 when the hour's PM10 and PM2.5 are within the study's `hourlyAirLimitParticulateMatterTenMicrogramsPerCubicMetre` and `hourlyAirLimitParticulateMatterTwoAndAHalfMicrogramsPerCubicMetre`, else 0 | with either particulate reading missing |
| `windowsOpenOnDailyAirHours` | 1 when the means of PM10 and of PM2.5 over the 24 hours of time up to and including the hour are within the study's `whoDailyLimitParticulateMatterTenMicrogramsPerCubicMetre` and `whoDailyLimitParticulateMatterTwoAndAHalfMicrogramsPerCubicMetre`, else 0. Readings at uneven instants count by when they fell | while either pollutant has fewer readings in the day than the study's `leastHourlyReadingsForADailyMean`, which starts at 18, the 75 % the European Union's air quality directive (2008/50/EC) asks of a daily mean. So the first 17 hours of a history have none |
| `designStrategyGroup` | the first design-strategy group whose tests the hour passes, as the API Field Guide's Climate table sets out. The groups are read from the model through the `designStrategyGroup` role, a group beneath a kind of group included, and tried by their `order`, then by identifier where two share one; each holds the tests it asks, as booleans such as `insideStillAirZone` or `reachedByDirectEvaporation`, and a test it leaves out is not asked. A group with no `order` is never tried. Either model mistake is logged once a run, naming the groups. The comfort zones use the study's `metabolicRateMet`, `summerClothingClo`, `winterClothingClo`, `stillAirMetresPerSecond` and `airMovementMetresPerSecond`; the humidity limit is `comfortHumidityRatioLimitGramsPerKilogram`, and the indirect stage's share `indirectEvaporativeStageShare` | with no temperature or no humidity; for every hour while the elevation or any of those inputs is unknown; and where no group the model holds takes the hour |

With the stamp it writes the study's outlines, `comfortZoneInStillAirOutline`,
`comfortZoneWithAirMovementOutline`, `directEvaporativeCoolingOutline` and `twoStageEvaporativeCoolingOutline`,
as the Field Guide describes them. An evaporative outline no recorded air reaches is written as the empty
Polygon, never left out, so a study never keeps an outline from inputs it no longer holds. While the elevation or
a comfort input is unknown, no outline is written.

The readings go through the bulk route, which sets off no recompute for each reading. Stomata then asks the
platform once to judge the site's history again, and last writes `climateWorkedOutAt` onto the study, at the
model's time as the platform tells it, never this machine's: a clock it cannot read fails the call as one the
orchestrator may try again. It writes
that stamp on every run that reaches the end, a study with no site or no history included, so the study leaves the
range that started it.

A model it cannot reach, or one that cannot take the readings now, is a failure the orchestrator may try again. A
subject naming no study, a study the model does not hold, or readings the model refuses fail the call. Neither
writes the stamp, so the study stays in the range.

Mycelium launches it on the port its service Thing names, the first time a pipeline calls the connection that
reaches it. The deploy's build of the services checkout builds it with the rest of the solution.
