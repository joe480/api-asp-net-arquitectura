using System.Collections.Concurrent;
using System.Diagnostics;

// API mínima de matrículas para ilustrar la MEDICIÓN de latencia de un ASR.
// Nota técnica: ejecutarla localmente NO es una prueba de rendimiento real.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<LatencyRecorder>();
builder.Services.AddSingleton<MatriculaStore>();

var app = builder.Build();
app.MapOpenApi();

// Middleware: mide el tiempo de cada solicitud, lo devuelve en un header,
// lo registra con un correlation ID y lo guarda para calcular percentiles.
app.Use(async (ctx, next) =>
{
    var recorder = ctx.RequestServices.GetRequiredService<LatencyRecorder>();
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;

    var sw = Stopwatch.StartNew();
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["X-Response-Time-ms"] = sw.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    });

    await next();
    sw.Stop();

    var path = ctx.Request.Path.Value ?? "";
    if (!path.StartsWith("/metrics"))
    {
        recorder.Record($"{ctx.Request.Method} {path}", sw.Elapsed.TotalMilliseconds);
    }
    if (sw.Elapsed.TotalMilliseconds > 200)
    {
        app.Logger.LogWarning("Solicitud lenta {Method} {Path} {ElapsedMs} ms CorrelationId={CorrelationId}",
            ctx.Request.Method, path, sw.Elapsed.TotalMilliseconds, correlationId);
    }
});

// Consulta de oferta de materias (operación crítica al inicio de semestre).
app.MapGet("/api/materias", async (MatriculaStore store) =>
{
    await Task.Delay(Random.Shared.Next(5, 25)); // simula acceso a BD / caché
    return Results.Ok(store.Materias);
});

// Inscripción a una materia (escritura con control de cupos).
app.MapPost("/api/matriculas", async (SolicitudMatricula req, MatriculaStore store) =>
{
    await Task.Delay(Random.Shared.Next(20, 60)); // simula transacción en BD
    var (ok, mensaje) = store.Inscribir(req.EstudianteId, req.MateriaId);
    return ok ? Results.Created($"/api/matriculas/{req.EstudianteId}", new { mensaje })
              : Results.Conflict(new { mensaje });
});

// Percentiles observados por endpoint (p50 / p95 / p99).
app.MapGet("/metrics/latency", (LatencyRecorder recorder) => Results.Ok(recorder.Summary()));
app.MapPost("/metrics/reset", (LatencyRecorder recorder) => { recorder.Reset(); return Results.NoContent(); });

app.Run();

record SolicitudMatricula(int EstudianteId, int MateriaId);
record Materia(int Id, string Nombre, int Cupos);

class MatriculaStore
{
    private readonly ConcurrentDictionary<int, int> _cuposDisponibles = new();
    private readonly ConcurrentDictionary<(int, int), bool> _inscripciones = new();
    public List<Materia> Materias { get; }

    public MatriculaStore()
    {
        Materias = Enumerable.Range(1, 20)
            .Select(i => new Materia(i, $"Materia {i}", 100_000))
            .ToList();
        foreach (var m in Materias) _cuposDisponibles[m.Id] = m.Cupos;
    }

    public (bool, string) Inscribir(int estudianteId, int materiaId)
    {
        if (!_cuposDisponibles.ContainsKey(materiaId)) return (false, "Materia inexistente");
        if (!_inscripciones.TryAdd((estudianteId, materiaId), true)) return (true, "Ya inscrito (idempotente)");
        var restantes = _cuposDisponibles.AddOrUpdate(materiaId, 0, (_, c) => c - 1);
        if (restantes < 0)
        {
            _cuposDisponibles.AddOrUpdate(materiaId, 0, (_, c) => c + 1);
            _inscripciones.TryRemove((estudianteId, materiaId), out _);
            return (false, "Sin cupos");
        }
        return (true, "Inscripción registrada");
    }
}

class LatencyRecorder
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _samples = new();

    public void Record(string key, double ms) => _samples.GetOrAdd(key, _ => new()).Add(ms);
    public void Reset() => _samples.Clear();

    public object Summary() => _samples.ToDictionary(kv => kv.Key, kv =>
    {
        var s = kv.Value.OrderBy(x => x).ToArray();
        return new
        {
            muestras = s.Length,
            p50 = Percentil(s, 50),
            p95 = Percentil(s, 95),
            p99 = Percentil(s, 99),
            max = s.Length > 0 ? Math.Round(s[^1], 2) : 0
        };
    });

    private static double Percentil(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
        return Math.Round(sorted[Math.Clamp(idx, 0, sorted.Length - 1)], 2);
    }
}
