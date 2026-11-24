# Actividad 3 – De requisito vago a ASR (API de matrículas + JMeter)

API mínima en ASP.NET Core (.NET 10) para ilustrar la **medición de latencia** de un ASR.
Una ejecución local NO es una prueba de rendimiento real (ver nota técnica en el documento).

## Ejecutar la API

```bash
cd MatriculasApi
dotnet run -c Release --urls http://localhost:5080
```

Endpoints:
- `GET  /api/materias` – consulta de oferta (simula 5–25 ms de acceso a datos)
- `POST /api/matriculas` – inscripción `{"estudianteId": 1, "materiaId": 2}` (simula 20–60 ms)
- `GET  /metrics/latency` – P50 / P95 / P99 por endpoint
- `POST /metrics/reset` – limpia las muestras

Cada respuesta incluye `X-Response-Time-ms` y `X-Correlation-Id`. Si una solicitud tarda más de 200 ms se registra un warning.

## Prueba de carga con JMeter

JMeter 5.6.3 instalado en `~/herramientas/apache-jmeter-5.6.3`.

```bash
cd jmeter
JM=~/herramientas/apache-jmeter-5.6.3/bin/jmeter

# Ejecución aislada (1 usuario)
$JM -n -t prueba-latencia-matriculas.jmx -Jusuarios=1 -Jrampa=1 -Jiteraciones=1 -l resultados/aislada.jtl

# Carga (200 usuarios, 10 iteraciones)
$JM -n -t prueba-latencia-matriculas.jmx -Jusuarios=200 -Jrampa=20 -Jiteraciones=10 \
    -l resultados/carga-200.jtl -e -o resultados/reporte-html-200
# Carga con 10.000 usuarios concurrentes (rampa corta + muchas iteraciones para que los 10k estén activos a la vez)
HEAP="-Xms2g -Xmx10g -Xss256k" $JM -n -t prueba-latencia-matriculas.jmx -Jusuarios=10000 -Jrampa=10 -Jiteraciones=150 \
    -l resultados/carga-10000.jtl -e -o resultados/reporte-html-10000
```

Para abrir el plan en la interfaz gráfica: `$JM -t prueba-latencia-matriculas.jmx`.
Reporte HTML: `jmeter/resultados/reporte-html-200/index.html`.

Propiedades: `host` (localhost), `port` (5080), `usuarios`, `rampa`, `iteraciones`.
