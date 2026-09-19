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
| walk | full | constant@3Mbit/s | 0.6761 | 0.595 | 0.0 | 0.0 | 2000.0 | 7.5 | 6.465 |
| walk | lod | constant@3Mbit/s | 0.1016 | 0.0562 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.483 |
| walk | roi | constant@3Mbit/s | 0.1021 | 0.0562 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.483 |
| walk | adaptive_geom | constant@3Mbit/s | 0.7839 | 0.7071 | 14.22 | 15.03 | 30.2 | 7.08 | 0.108 |
| walk | adaptive | constant@3Mbit/s | 0.7808 | 0.705 | 15.03 | 14.7 | 35.3 | 7.086 | 0.106 |
| walk | full | constant@10Mbit/s | 0.6852 | 0.5996 | 0.0 | 0.0 | 2000.0 | 25.0 | 23.936 |
| walk | lod | constant@10Mbit/s | 0.133 | 0.0562 | 0.0 | 0.31 | 4685.6 | 25.0 | 24.291 |
| walk | roi | constant@10Mbit/s | 0.1021 | 0.0562 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.954 |
| walk | adaptive_geom | constant@10Mbit/s | 0.8642 | 0.8067 | 15.17 | 15.17 | 25.0 | 23.381 | 0.0 |
| walk | adaptive | constant@10Mbit/s | 0.8633 | 0.8097 | 15.1 | 15.15 | 25.1 | 22.972 | 0.0 |
| walk | full | constant@25Mbit/s | 0.6875 | 0.604 | 0.0 | 0.0 | 2000.0 | 62.5 | 61.373 |
| walk | lod | constant@25Mbit/s | 0.7181 | 0.6189 | 0.07 | 4.9 | 5938.1 | 62.5 | 13.681 |
| walk | roi | constant@25Mbit/s | 0.3725 | 0.1762 | 0.0 | 4.18 | 1497.3 | 62.5 | 15.025 |
| walk | adaptive_geom | constant@25Mbit/s | 0.9176 | 0.8682 | 15.24 | 15.09 | 24.2 | 54.349 | 0.008 |
| walk | adaptive | constant@25Mbit/s | 0.9171 | 0.8845 | 15.3 | 15.07 | 25.1 | 54.725 | 0.03 |
| walk | full | constant@50Mbit/s | 0.7584 | 0.6371 | 14.49 | 0.04 | 2644.0 | 125.0 | 8.206 |
| walk | lod | constant@50Mbit/s | 0.8024 | 0.6856 | 2.22 | 9.33 | 3771.8 | 125.0 | 24.936 |
| walk | roi | constant@50Mbit/s | 0.6085 | 0.2791 | 0.34 | 8.38 | 1730.3 | 123.456 | 27.184 |
| walk | adaptive_geom | constant@50Mbit/s | 0.9445 | 0.9096 | 15.17 | 15.2 | 21.9 | 106.577 | 0.03 |
| walk | adaptive | constant@50Mbit/s | 0.9436 | 0.9095 | 15.24 | 15.2 | 21.4 | 103.419 | 0.006 |
| look_around | full | constant@3Mbit/s | 0.7169 | 0.5176 | 0.0 | 0.0 | 1647.8 | 3.75 | 2.703 |
| look_around | lod | constant@3Mbit/s | 0.1525 | 0.0361 | 0.0 | 0.0 | 1647.8 | 3.75 | 3.722 |
| look_around | roi | constant@3Mbit/s | 0.3199 | 0.0372 | 0.0 | 0.0 | 1647.8 | 3.183 | 3.153 |
| look_around | adaptive_geom | constant@3Mbit/s | 0.8075 | 0.6048 | 13.79 | 15.13 | 43.6 | 2.646 | 0.003 |
| look_around | adaptive | constant@3Mbit/s | 0.7898 | 0.3764 | 14.91 | 14.83 | 45.8 | 2.672 | 0.014 |
| look_around | full | constant@10Mbit/s | 0.7339 | 0.5543 | 0.0 | 0.0 | 1647.8 | 12.5 | 11.394 |
| look_around | lod | constant@10Mbit/s | 0.1525 | 0.0361 | 0.0 | 0.0 | 1647.8 | 12.5 | 12.414 |
| look_around | roi | constant@10Mbit/s | 0.3256 | 0.0372 | 0.0 | 0.09 | 1579.3 | 10.606 | 10.196 |
| look_around | adaptive_geom | constant@10Mbit/s | 0.8796 | 0.8047 | 15.28 | 15.15 | 28.2 | 9.921 | 0.017 |
| look_around | adaptive | constant@10Mbit/s | 0.8754 | 0.8047 | 15.84 | 15.05 | 26.0 | 9.556 | 0.015 |
| look_around | full | constant@25Mbit/s | 0.7375 | 0.5673 | 0.0 | 0.0 | 1647.8 | 31.25 | 30.019 |
| look_around | lod | constant@25Mbit/s | 0.3714 | 0.16 | 0.0 | 2.4 | 1419.2 | 31.25 | 7.706 |
| look_around | roi | constant@25Mbit/s | 0.4749 | 0.2216 | 0.0 | 3.76 | 1474.7 | 26.632 | 6.254 |
| look_around | adaptive_geom | constant@25Mbit/s | 0.927 | 0.8811 | 15.65 | 15.19 | 27.8 | 23.841 | 0.02 |
| look_around | adaptive | constant@25Mbit/s | 0.9252 | 0.8811 | 15.47 | 15.18 | 26.8 | 23.839 | 0.069 |
| look_around | full | constant@50Mbit/s | 0.7753 | 0.576 | 13.98 | 0.04 | 1550.2 | 62.5 | 4.411 |
| look_around | lod | constant@50Mbit/s | 0.422 | 0.236 | 0.0 | 5.0 | 1258.3 | 62.5 | 14.133 |
| look_around | roi | constant@50Mbit/s | 0.7981 | 0.3648 | 0.0 | 7.79 | 729.9 | 50.653 | 8.766 |
| look_around | adaptive_geom | constant@50Mbit/s | 0.9498 | 0.9073 | 15.84 | 15.23 | 23.7 | 44.421 | 0.0 |
| look_around | adaptive | constant@50Mbit/s | 0.9482 | 0.9074 | 15.28 | 15.29 | 25.8 | 42.88 | 0.016 |

## Physics laboratory

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | constant@3Mbit/s | 0.6349 | 0.3508 | 0.0 | 0.0 | 2000.0 | 7.5 | 5.539 |
| walk | lod | constant@3Mbit/s | 0.0519 | 0.0162 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.244 |
| walk | roi | constant@3Mbit/s | 0.0528 | 0.0162 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.244 |
| walk | adaptive_geom | constant@3Mbit/s | 0.7403 | 0.5537 | 10.97 | 14.03 | 111.9 | 7.396 | 0.03 |
| walk | adaptive | constant@3Mbit/s | 0.7368 | 0.5285 | 14.33 | 12.95 | 131.4 | 7.386 | 0.01 |
| walk | full | constant@10Mbit/s | 0.6619 | 0.5866 | 0.0 | 0.0 | 2000.0 | 25.0 | 23.009 |
| walk | lod | constant@10Mbit/s | 0.0541 | 0.0162 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.715 |
| walk | roi | constant@10Mbit/s | 0.055 | 0.0162 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.715 |
| walk | adaptive_geom | constant@10Mbit/s | 0.8531 | 0.8059 | 15.0 | 15.05 | 25.5 | 23.654 | 0.014 |
| walk | adaptive | constant@10Mbit/s | 0.8533 | 0.8061 | 14.93 | 14.97 | 28.0 | 23.19 | 0.029 |
| walk | full | constant@25Mbit/s | 0.6699 | 0.6017 | 0.0 | 0.0 | 2000.0 | 62.5 | 60.447 |
| walk | lod | constant@25Mbit/s | 0.1555 | 0.0401 | 0.0 | 3.73 | 1467.4 | 62.5 | 10.702 |
| walk | roi | constant@25Mbit/s | 0.2663 | 0.085 | 0.0 | 4.18 | 1355.9 | 62.5 | 10.549 |
| walk | adaptive_geom | constant@25Mbit/s | 0.9162 | 0.8901 | 15.3 | 15.26 | 24.3 | 55.373 | 0.013 |
| walk | adaptive | constant@25Mbit/s | 0.9161 | 0.8899 | 15.22 | 15.23 | 26.4 | 55.345 | 0.064 |
| walk | full | constant@50Mbit/s | 0.7175 | 0.6118 | 14.48 | 0.04 | 1806.8 | 125.0 | 17.323 |
| walk | lod | constant@50Mbit/s | 0.4822 | 0.3142 | 0.0 | 6.71 | 3005.5 | 125.0 | 18.417 |
| walk | roi | constant@50Mbit/s | 0.5488 | 0.3224 | 0.37 | 7.97 | 1394.3 | 123.375 | 17.273 |
| walk | adaptive_geom | constant@50Mbit/s | 0.943 | 0.9124 | 15.15 | 15.11 | 24.1 | 101.408 | 0.0 |
| walk | adaptive | constant@50Mbit/s | 0.943 | 0.9121 | 15.0 | 15.11 | 24.5 | 100.131 | 0.013 |
| bench_inspection | full | constant@3Mbit/s | 0.6265 | 0.5217 | 0.0 | 0.0 | 2000.0 | 4.5 | 2.526 |
| bench_inspection | lod | constant@3Mbit/s | 0.0957 | 0.0582 | 0.0 | 0.0 | 2000.0 | 4.5 | 4.232 |
| bench_inspection | roi | constant@3Mbit/s | 0.1037 | 0.0582 | 0.0 | 0.0 | 2000.0 | 4.5 | 4.232 |
| bench_inspection | adaptive_geom | constant@3Mbit/s | 0.6873 | 0.3114 | 10.55 | 12.0 | 126.7 | 4.273 | 0.041 |
| bench_inspection | adaptive | constant@3Mbit/s | 0.6697 | 0.0815 | 13.71 | 11.46 | 124.4 | 4.336 | 0.005 |
| bench_inspection | full | constant@10Mbit/s | 0.6479 | 0.5268 | 0.0 | 0.0 | 2000.0 | 15.0 | 12.968 |
| bench_inspection | lod | constant@10Mbit/s | 0.1001 | 0.0582 | 0.0 | 0.0 | 2000.0 | 15.0 | 14.673 |
| bench_inspection | roi | constant@10Mbit/s | 0.1088 | 0.0582 | 0.0 | 0.0 | 2000.0 | 15.0 | 14.673 |
| bench_inspection | adaptive_geom | constant@10Mbit/s | 0.8268 | 0.7381 | 14.88 | 15.04 | 42.8 | 14.583 | 0.018 |
| bench_inspection | adaptive | constant@10Mbit/s | 0.8218 | 0.738 | 14.77 | 14.89 | 48.6 | 14.512 | 0.044 |
| bench_inspection | full | constant@25Mbit/s | 0.6539 | 0.5268 | 0.0 | 0.0 | 2000.0 | 37.5 | 35.343 |
| bench_inspection | lod | constant@25Mbit/s | 0.212 | 0.1164 | 0.0 | 2.76 | 1698.5 | 37.5 | 6.522 |
| bench_inspection | roi | constant@25Mbit/s | 0.2464 | 0.1407 | 0.0 | 3.13 | 1671.5 | 37.5 | 6.531 |
| bench_inspection | adaptive_geom | constant@25Mbit/s | 0.8979 | 0.8573 | 14.88 | 15.26 | 36.8 | 34.935 | 0.032 |
| bench_inspection | adaptive | constant@25Mbit/s | 0.8957 | 0.8573 | 15.0 | 15.22 | 36.9 | 34.937 | 0.077 |
| bench_inspection | full | constant@50Mbit/s | 0.7028 | 0.5269 | 14.18 | 0.06 | 1658.1 | 75.0 | 10.261 |
| bench_inspection | lod | constant@50Mbit/s | 0.4612 | 0.2253 | 0.12 | 4.64 | 1663.3 | 75.0 | 11.513 |
| bench_inspection | roi | constant@50Mbit/s | 0.5176 | 0.3457 | 0.0 | 6.04 | 1278.1 | 75.0 | 12.041 |
| bench_inspection | adaptive_geom | constant@50Mbit/s | 0.9292 | 0.8864 | 15.0 | 15.24 | 33.3 | 64.467 | 0.091 |
| bench_inspection | adaptive | constant@50Mbit/s | 0.9283 | 0.8864 | 14.88 | 15.2 | 31.4 | 63.741 | 0.0 |

## Chemistry laboratory

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | constant@3Mbit/s | 0.4254 | 0.1844 | 0.0 | 0.0 | 2000.0 | 7.5 | 5.371 |
| walk | lod | constant@3Mbit/s | 0.0622 | 0.038 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.058 |
| walk | roi | constant@3Mbit/s | 0.0622 | 0.038 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.058 |
| walk | adaptive_geom | constant@3Mbit/s | 0.5061 | 0.2424 | 9.45 | 10.49 | 581.4 | 6.959 | 0.001 |
| walk | adaptive | constant@3Mbit/s | 0.5023 | 0.1295 | 13.45 | 9.15 | 730.0 | 6.967 | 0.002 |
| walk | full | constant@10Mbit/s | 0.4424 | 0.2591 | 0.0 | 0.0 | 2000.0 | 25.0 | 22.842 |
| walk | lod | constant@10Mbit/s | 0.0692 | 0.038 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.529 |
| walk | roi | constant@10Mbit/s | 0.0692 | 0.038 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.529 |
| walk | adaptive_geom | constant@10Mbit/s | 0.6612 | 0.5742 | 14.75 | 14.79 | 46.0 | 23.028 | 0.002 |
| walk | adaptive | constant@10Mbit/s | 0.6589 | 0.5742 | 14.85 | 14.65 | 53.0 | 22.996 | 0.012 |
| walk | full | constant@25Mbit/s | 0.4584 | 0.3737 | 0.0 | 0.0 | 2000.0 | 62.5 | 60.279 |
| walk | lod | constant@25Mbit/s | 0.1533 | 0.0861 | 0.0 | 2.22 | 1949.1 | 62.5 | 10.54 |
| walk | roi | constant@25Mbit/s | 0.1533 | 0.0861 | 0.0 | 2.22 | 1949.1 | 62.5 | 10.54 |
| walk | adaptive_geom | constant@25Mbit/s | 0.7669 | 0.7047 | 14.9 | 14.99 | 35.0 | 58.469 | 0.026 |
| walk | adaptive | constant@25Mbit/s | 0.7659 | 0.7046 | 14.95 | 14.93 | 35.4 | 58.033 | 0.0 |
| walk | full | constant@50Mbit/s | 0.5601 | 0.4431 | 14.55 | 0.02 | 1929.3 | 125.0 | 6.722 |
| walk | lod | constant@50Mbit/s | 0.2167 | 0.1423 | 0.0 | 4.45 | 1648.5 | 125.0 | 19.961 |
| walk | roi | constant@50Mbit/s | 0.2167 | 0.1423 | 0.0 | 4.45 | 1648.5 | 125.0 | 19.961 |
| walk | adaptive_geom | constant@50Mbit/s | 0.8507 | 0.8102 | 14.95 | 15.08 | 28.4 | 117.462 | 0.011 |
| walk | adaptive | constant@50Mbit/s | 0.8502 | 0.8102 | 15.0 | 15.07 | 28.4 | 117.48 | 0.048 |
| close_demo | full | constant@3Mbit/s | 0.4916 | 0.2443 | 0.0 | 0.0 | 2000.0 | 4.5 | 2.358 |
| close_demo | lod | constant@3Mbit/s | 0.0491 | 0.0253 | 0.0 | 0.0 | 2000.0 | 4.5 | 4.472 |
| close_demo | roi | constant@3Mbit/s | 0.0491 | 0.0253 | 0.0 | 0.0 | 2000.0 | 4.5 | 4.472 |
| close_demo | adaptive_geom | constant@3Mbit/s | 0.562 | 0.294 | 13.58 | 8.88 | 508.9 | 3.951 | 0.005 |
| close_demo | adaptive | constant@3Mbit/s | 0.5283 | 0.0309 | 13.75 | 8.68 | 507.4 | 3.924 | 0.005 |
| close_demo | full | constant@10Mbit/s | 0.5224 | 0.3604 | 0.0 | 0.0 | 2000.0 | 15.0 | 12.8 |
| close_demo | lod | constant@10Mbit/s | 0.0491 | 0.0253 | 0.0 | 0.0 | 2000.0 | 15.0 | 14.914 |
| close_demo | roi | constant@10Mbit/s | 0.0491 | 0.0253 | 0.0 | 0.0 | 2000.0 | 15.0 | 14.914 |
| close_demo | adaptive_geom | constant@10Mbit/s | 0.6995 | 0.4041 | 14.58 | 14.4 | 36.5 | 12.851 | 0.003 |
| close_demo | adaptive | constant@10Mbit/s | 0.6883 | 0.4357 | 14.58 | 14.24 | 61.9 | 12.837 | 0.002 |
| close_demo | full | constant@25Mbit/s | 0.5289 | 0.417 | 0.0 | 0.0 | 2000.0 | 37.5 | 35.175 |
| close_demo | lod | constant@25Mbit/s | 0.113 | 0.0728 | 0.0 | 2.35 | 1673.4 | 37.5 | 6.674 |
| close_demo | roi | constant@25Mbit/s | 0.113 | 0.0728 | 0.0 | 2.35 | 1673.4 | 37.5 | 6.674 |
| close_demo | adaptive_geom | constant@25Mbit/s | 0.7926 | 0.7212 | 14.92 | 14.72 | 36.8 | 33.465 | 0.05 |
| close_demo | adaptive | constant@25Mbit/s | 0.7866 | 0.7213 | 14.92 | 14.72 | 52.2 | 33.465 | 0.051 |
| close_demo | full | constant@50Mbit/s | 0.6323 | 0.5013 | 14.33 | 0.01 | 1928.2 | 75.0 | 4.187 |
| close_demo | lod | constant@50Mbit/s | 0.179 | 0.1293 | 0.0 | 4.75 | 1312.0 | 75.0 | 11.965 |
| close_demo | roi | constant@50Mbit/s | 0.3617 | 0.1293 | 0.0 | 4.69 | 1312.9 | 75.0 | 11.588 |
| close_demo | adaptive_geom | constant@50Mbit/s | 0.863 | 0.8179 | 14.92 | 14.87 | 35.8 | 68.413 | 0.054 |
| close_demo | adaptive | constant@50Mbit/s | 0.8618 | 0.8179 | 14.92 | 14.86 | 38.2 | 68.413 | 0.02 |

## PE class (sports hall)

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | constant@3Mbit/s | 0.1276 | 0.0919 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.437 |
| walk | lod | constant@3Mbit/s | 0.0616 | 0.0331 | 0.0 | 0.02 | 2371.4 | 7.5 | 7.343 |
| walk | roi | constant@3Mbit/s | 0.0603 | 0.0331 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.454 |
| walk | adaptive_geom | constant@3Mbit/s | 0.3532 | 0.2794 | 6.5 | 11.33 | 719.3 | 7.184 | 0.003 |
| walk | adaptive | constant@3Mbit/s | 0.3482 | 0.2791 | 13.85 | 9.98 | 699.7 | 7.312 | 0.004 |
| walk | full | constant@10Mbit/s | 0.128 | 0.0921 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.908 |
| walk | lod | constant@10Mbit/s | 0.0752 | 0.0331 | 0.0 | 0.32 | 2821.4 | 25.0 | 22.768 |
| walk | roi | constant@10Mbit/s | 0.0605 | 0.0331 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.924 |
| walk | adaptive_geom | constant@10Mbit/s | 0.6054 | 0.5193 | 15.05 | 15.04 | 25.8 | 24.346 | 0.004 |
| walk | adaptive | constant@10Mbit/s | 0.6051 | 0.5194 | 15.1 | 15.06 | 21.8 | 24.24 | 0.009 |
| walk | full | constant@25Mbit/s | 0.1281 | 0.0921 | 0.0 | 0.0 | 2000.0 | 62.5 | 62.345 |
| walk | lod | constant@25Mbit/s | 0.2405 | 0.1301 | 0.0 | 2.2 | 4261.4 | 62.5 | 17.708 |
| walk | roi | constant@25Mbit/s | 0.1909 | 0.1433 | 0.0 | 1.61 | 2366.7 | 62.5 | 18.993 |
| walk | adaptive_geom | constant@25Mbit/s | 0.7588 | 0.6839 | 15.2 | 15.04 | 23.7 | 60.084 | 0.016 |
| walk | adaptive | constant@25Mbit/s | 0.758 | 0.6831 | 15.2 | 15.06 | 21.2 | 59.739 | 0.016 |
| walk | full | constant@50Mbit/s | 0.2053 | 0.1483 | 14.75 | 0.02 | 2474.8 | 125.0 | 19.349 |
| walk | lod | constant@50Mbit/s | 0.4309 | 0.2738 | 1.65 | 5.03 | 5082.7 | 125.0 | 30.745 |
| walk | roi | constant@50Mbit/s | 0.3312 | 0.2547 | 0.0 | 3.13 | 2195.1 | 125.0 | 38.404 |
| walk | adaptive_geom | constant@50Mbit/s | 0.8469 | 0.7829 | 15.15 | 15.06 | 22.3 | 118.548 | 0.009 |
| walk | adaptive | constant@50Mbit/s | 0.8462 | 0.7787 | 15.15 | 15.07 | 21.8 | 118.133 | 0.016 |
| fast_sweep | full | constant@3Mbit/s | 0.2782 | 0.0929 | 0.0 | 0.0 | 1687.7 | 3.75 | 3.674 |
| fast_sweep | lod | constant@3Mbit/s | 0.221 | 0.0314 | 0.0 | 0.0 | 1687.7 | 3.75 | 3.707 |
| fast_sweep | roi | constant@3Mbit/s | 0.2369 | 0.047 | 0.0 | 0.0 | 1687.7 | 3.257 | 3.213 |
| fast_sweep | adaptive_geom | constant@3Mbit/s | 0.4822 | 0.2782 | 4.78 | 12.79 | 308.2 | 2.918 | 0.004 |
| fast_sweep | adaptive | constant@3Mbit/s | 0.4792 | 0.2782 | 8.81 | 12.07 | 338.6 | 2.949 | 0.009 |
| fast_sweep | full | constant@10Mbit/s | 0.2786 | 0.0929 | 0.0 | 0.0 | 1687.7 | 12.5 | 12.366 |
| fast_sweep | lod | constant@10Mbit/s | 0.221 | 0.0314 | 0.0 | 0.0 | 1687.7 | 12.5 | 12.399 |
| fast_sweep | roi | constant@10Mbit/s | 0.2447 | 0.047 | 0.0 | 0.05 | 1667.2 | 10.855 | 10.319 |
| fast_sweep | adaptive_geom | constant@10Mbit/s | 0.6674 | 0.5303 | 14.93 | 15.02 | 29.8 | 9.927 | 0.008 |
| fast_sweep | adaptive | constant@10Mbit/s | 0.667 | 0.5303 | 15.07 | 15.04 | 25.9 | 9.916 | 0.008 |
| fast_sweep | full | constant@25Mbit/s | 0.2787 | 0.0929 | 0.0 | 0.0 | 1687.7 | 31.25 | 30.991 |
| fast_sweep | lod | constant@25Mbit/s | 0.3351 | 0.1275 | 0.0 | 1.26 | 1606.0 | 31.25 | 9.25 |
| fast_sweep | roi | constant@25Mbit/s | 0.4178 | 0.1766 | 0.0 | 1.79 | 1570.1 | 26.987 | 7.514 |
| fast_sweep | adaptive_geom | constant@25Mbit/s | 0.7968 | 0.7009 | 14.93 | 15.05 | 28.3 | 24.981 | 0.009 |
| fast_sweep | adaptive | constant@25Mbit/s | 0.793 | 0.6862 | 15.07 | 15.08 | 25.9 | 24.436 | 0.008 |
| fast_sweep | full | constant@50Mbit/s | 0.3202 | 0.1369 | 14.48 | 0.03 | 1661.5 | 62.5 | 9.933 |
| fast_sweep | lod | constant@50Mbit/s | 0.4294 | 0.2769 | 0.0 | 2.53 | 1542.0 | 62.5 | 18.149 |
| fast_sweep | roi | constant@50Mbit/s | 0.5274 | 0.302 | 0.0 | 3.69 | 1403.3 | 53.539 | 13.993 |
| fast_sweep | adaptive_geom | constant@50Mbit/s | 0.8688 | 0.7851 | 15.07 | 15.03 | 28.3 | 47.371 | 0.024 |
| fast_sweep | adaptive | constant@50Mbit/s | 0.8676 | 0.7851 | 15.07 | 15.04 | 27.4 | 47.189 | 0.022 |

## Hazard lab (virtual only)

| path | policy | link | QoE | QoE p05 | instr fps | others fps | stale ms | sent MB | wasted MB |
|---|---|---|---|---|---|---|---|---|---|
| walk | full | constant@3Mbit/s | 0.561 | 0.5176 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.32 |
| walk | lod | constant@3Mbit/s | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.481 |
| walk | roi | constant@3Mbit/s | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 7.5 | 7.481 |
| walk | adaptive_geom | constant@3Mbit/s | 0.6594 | 0.6017 | 11.65 | 14.74 | 75.7 | 6.453 | 0.0 |
| walk | adaptive | constant@3Mbit/s | 0.6607 | 0.6139 | 14.9 | 13.2 | 211.5 | 6.542 | 0.001 |
| walk | full | constant@10Mbit/s | 0.5637 | 0.5189 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.791 |
| walk | lod | constant@10Mbit/s | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.952 |
| walk | roi | constant@10Mbit/s | 0.086 | 0.0705 | 0.0 | 0.0 | 2000.0 | 25.0 | 24.952 |
| walk | adaptive_geom | constant@10Mbit/s | 0.7745 | 0.7347 | 15.05 | 15.06 | 24.8 | 22.005 | 0.008 |
| walk | adaptive | constant@10Mbit/s | 0.7739 | 0.7351 | 15.05 | 15.02 | 28.8 | 21.968 | 0.004 |
| walk | full | constant@25Mbit/s | 0.5644 | 0.5194 | 0.0 | 0.0 | 2000.0 | 62.5 | 62.229 |
| walk | lod | constant@25Mbit/s | 0.1561 | 0.1288 | 0.0 | 2.8 | 2115.9 | 62.5 | 14.832 |
| walk | roi | constant@25Mbit/s | 0.1561 | 0.1288 | 0.0 | 2.8 | 2115.9 | 62.5 | 14.832 |
| walk | adaptive_geom | constant@25Mbit/s | 0.8538 | 0.8201 | 15.1 | 15.1 | 23.7 | 57.734 | 0.0 |
| walk | adaptive | constant@25Mbit/s | 0.8537 | 0.8217 | 15.0 | 15.1 | 25.1 | 57.282 | 0.012 |
| walk | full | constant@50Mbit/s | 0.6616 | 0.5849 | 14.75 | 0.05 | 1791.3 | 125.0 | 6.787 |
| walk | lod | constant@50Mbit/s | 0.2249 | 0.1918 | 0.0 | 5.56 | 1533.1 | 125.0 | 28.167 |
| walk | roi | constant@50Mbit/s | 0.2249 | 0.1918 | 0.0 | 5.56 | 1533.1 | 125.0 | 28.167 |
| walk | adaptive_geom | constant@50Mbit/s | 0.9005 | 0.8745 | 15.05 | 15.11 | 23.7 | 111.538 | 0.026 |
| walk | adaptive | constant@50Mbit/s | 0.9005 | 0.8745 | 15.05 | 15.1 | 23.0 | 109.752 | 0.001 |
