## Scenarios

| scenario | objects | static / live | triangles | points | static MB | live capture |
|---|---|---|---|---|---|---|
| Lecture classroom | 42 | 35 / 7 | 147,576 | 203,947 | 1.02 | 163 Mbit/s |
| Physics laboratory | 39 | 30 / 9 | 285,364 | 262,920 | 1.95 | 210 Mbit/s |
| Chemistry laboratory | 27 | 18 / 9 | 304,252 | 267,923 | 2.12 | 215 Mbit/s |
| PE class (sports hall) | 21 | 8 / 13 | 7,744 | 318,884 | 0.05 | 252 Mbit/s |
| Hazard lab (virtual only) | 21 | 12 / 9 | 25,732 | 251,931 | 0.17 | 202 Mbit/s |

## Lecture classroom

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.6844 | 0.5996 | 0.0 | 0.0 | 2000.0 | 17.469 | 16.432 |
| walk | lod | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.1326 | 0.0562 | 0.0 | 0.28 | 4687.1 | 17.469 | 16.831 |
| walk | roi | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.102 | 0.0562 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.454 |
| walk | adaptive_geom | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.8273 | 0.7102 | 14.7 | 14.8 | 89.3 | 16.437 | 0.109 |
| walk | adaptive | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.8253 | 0.7102 | 14.83 | 14.77 | 91.2 | 16.312 | 0.054 |

## Physics laboratory

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.6554 | 0.5037 | 0.0 | 0.0 | 2000.0 | 17.469 | 14.981 |
| walk | lod | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0514 | 0.0162 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.213 |
| walk | roi | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0522 | 0.0162 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.214 |
| walk | adaptive_geom | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.8028 | 0.7047 | 13.21 | 14.48 | 77.2 | 16.607 | 0.118 |
| walk | adaptive | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.8003 | 0.7074 | 14.33 | 14.12 | 151.1 | 16.304 | 0.141 |

## Chemistry laboratory

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.4394 | 0.2585 | 0.0 | 0.0 | 2000.0 | 17.469 | 15.279 |
| walk | lod | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0671 | 0.038 | 0.0 | 0.0 | 2000.0 | 17.469 | 16.816 |
| walk | roi | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0671 | 0.038 | 0.0 | 0.0 | 2000.0 | 17.469 | 16.816 |
| walk | adaptive_geom | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.5969 | 0.4534 | 13.15 | 11.22 | 553.7 | 16.436 | 0.005 |
| walk | adaptive | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.5972 | 0.4526 | 14.65 | 10.46 | 435.7 | 16.535 | 0.008 |

## PE class (sports hall)

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.1279 | 0.092 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.406 |
| walk | lod | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0744 | 0.0331 | 0.0 | 0.3 | 2821.8 | 17.469 | 15.266 |
| walk | roi | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.0604 | 0.0331 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.422 |
| walk | adaptive_geom | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.4814 | 0.2365 | 8.85 | 12.01 | 454.6 | 17.379 | 0.049 |
| walk | adaptive | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.4814 | 0.2366 | 10.2 | 11.83 | 430.4 | 17.324 | 0.029 |

## Hazard lab (virtual only)

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.5629 | 0.5189 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.256 |
| walk | lod | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.449 |
| walk | roi | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 17.469 | 17.449 |
| walk | adaptive_geom | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.7266 | 0.6051 | 13.1 | 14.38 | 182.3 | 15.844 | 0.007 |
| walk | adaptive | step@10Mbit/s period=6s depth=0.25 loss=5.0% | 0.7253 | 0.6101 | 14.9 | 13.22 | 228.8 | 15.801 | 0.003 |
