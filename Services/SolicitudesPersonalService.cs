using System;
using System.Collections.Generic;
using BackendRequisicionPersonal.Models;
using IBM.Data.Db2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Services
{
    public class SolicitudesPersonalService
    {
        private readonly string _connectionString;
        private readonly ILogger<SolicitudesPersonalService> _logger;

        public SolicitudesPersonalService(
            IConfiguration configuration,
            IWebHostEnvironment env,
            ILogger<SolicitudesPersonalService> logger)
        {
            _logger = logger;
            _connectionString = env.IsDevelopment()
                ? configuration.GetConnectionString("InformixConnection")
                : configuration.GetConnectionString("InformixConnectionProduction");

            _logger.LogInformation("Cadena de conexión seleccionada: {ConnName}", env.IsDevelopment() ? "InformixConnection" : "InformixConnectionProduction");
        }

        public bool TestConnection()
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                var ok = cn.State == System.Data.ConnectionState.Open;
                cn.Close();
                _logger.LogInformation("TestConnection => {Estado}", ok ? "OK" : "FAIL");
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al probar conexión a BD");
                return false;
            }
        }

        public int Insertar(SolicitudPersonalDto x)
        {
            using var cn = new DB2Connection(_connectionString);
            cn.Open();
            using var tx = cn.BeginTransaction();

            try
            {
                // IMPORTANTE: Esta lista de columnas coincide con tu CREATE TABLE (sin usuario_solicitante ni correo_vp).
                var sql = @"
                INSERT INTO solicitudes_aprobaciones_personal
                (
                  id_solicitante, tipo, fecha_solicitud, vicepresidencia, nombre_vp,
                  jefe_inmediato, cargo_requerido, centro_costos, horario_trabajo, dias_laborales,
                  salario_basico, tipo_solicitud, tipo_contrato, meses_contrato, ciudad_trabajo,
                  justificacion, correo_jefe, canal, area, gerente_division, gerente_canal,
                  terr_asignado, cobro_automatico, zona_ciudades, clientes_cargo, canales_cargo,
                  auxilio_movilizacion, salario_garantizado, meses_garantizado, promedio_variable, requiere_moto,
                  correo_gerente_canal, correo_gerente_division, area_solicitante, cargo_jefe_inmediato,
                  centro_costos_f, hora_inicio, hora_fin, activar_proceso_por, persona_reemplaza, tipo_jornada,
                  estado_req, estado, nivel_aprobacion, creado_en, fecha_envio_aprobacion, fecha_aprobacion,
                  nombre_candidato, doc_candidato, salario_asignado, fecha_ingreso, aprobaciones_ingreso
                )
                VALUES
                (
                  @id_solicitante, @tipo, @fecha_solicitud, @vicepresidencia, @nombre_vp,
                  @jefe_inmediato, @cargo_requerido, @centro_costos, @horario_trabajo, @dias_laborales,
                  @salario_basico, @tipo_solicitud, @tipo_contrato, @meses_contrato, @ciudad_trabajo,
                  @justificacion, @correo_jefe, @canal, @area, @gerente_division, @gerente_canal,
                  @terr_asignado, @cobro_automatico, @zona_ciudades, @clientes_cargo, @canales_cargo,
                  @auxilio_movilizacion, @salario_garantizado, @meses_garantizado, @promedio_variable, @requiere_moto,
                  @correo_gerente_canal, @correo_gerente_division, @area_solicitante, @cargo_jefe_inmediato,
                  @centro_costos_f, @hora_inicio, @hora_fin, @activar_proceso_por, @persona_reemplaza, @tipo_jornada,
                  @estado_req, @estado, @nivel_aprobacion, @creado_en, @fecha_envio_aprobacion, @fecha_aprobacion,
                  @nombre_candidato, @doc_candidato, @salario_asignado, @fecha_ingreso, @aprobaciones_ingreso
                )";

                using var cmd = new DB2Command(sql, cn, tx);
                cmd.Parameters.AddRange(new[]
                {
                    new DB2Parameter("@id_solicitante", (object?)x.IdSolicitante ?? DBNull.Value),
                    new DB2Parameter("@tipo", x.Tipo ?? (object)DBNull.Value),
                    new DB2Parameter("@fecha_solicitud", x.FechaSolicitud ?? (object)DBNull.Value),
                    new DB2Parameter("@vicepresidencia", x.Vicepresidencia ?? (object)DBNull.Value),
                    new DB2Parameter("@nombre_vp", x.NombreVp ?? (object)DBNull.Value),

                    new DB2Parameter("@jefe_inmediato", x.JefeInmediato ?? (object)DBNull.Value),
                    new DB2Parameter("@cargo_requerido", x.CargoRequerido ?? (object)DBNull.Value),
                    new DB2Parameter("@centro_costos", x.CentroCostos ?? (object)DBNull.Value),
                    new DB2Parameter("@horario_trabajo", x.HorarioTrabajo ?? (object)DBNull.Value),
                    new DB2Parameter("@dias_laborales", x.DiasLaborales ?? (object)DBNull.Value),

                    new DB2Parameter("@salario_basico", x.SalarioBasico ?? (object)DBNull.Value),
                    new DB2Parameter("@tipo_solicitud", x.TipoSolicitud ?? (object)DBNull.Value),
                    new DB2Parameter("@tipo_contrato", x.TipoContrato ?? (object)DBNull.Value),
                    new DB2Parameter("@meses_contrato", x.MesesContrato ?? (object)DBNull.Value),
                    new DB2Parameter("@ciudad_trabajo", x.CiudadTrabajo ?? (object)DBNull.Value),

                    new DB2Parameter("@justificacion", x.Justificacion ?? (object)DBNull.Value),
                    new DB2Parameter("@correo_jefe", x.CorreoJefe ?? (object)DBNull.Value),
                    new DB2Parameter("@canal", x.Canal ?? (object)DBNull.Value),
                    new DB2Parameter("@area", x.Area ?? (object)DBNull.Value),
                    new DB2Parameter("@gerente_division", x.GerenteDivision ?? (object)DBNull.Value),
                    new DB2Parameter("@gerente_canal", x.GerenteCanal ?? (object)DBNull.Value),

                    new DB2Parameter("@terr_asignado", x.TerrAsignado ?? (object)DBNull.Value),
                    new DB2Parameter("@cobro_automatico", x.CobroAutomatico ?? (object)DBNull.Value),
                    new DB2Parameter("@zona_ciudades", x.ZonaCiudades ?? (object)DBNull.Value),
                    new DB2Parameter("@clientes_cargo", x.ClientesCargo ?? (object)DBNull.Value),
                    new DB2Parameter("@canales_cargo", x.CanalesCargo ?? (object)DBNull.Value),

                    new DB2Parameter("@auxilio_movilizacion", x.AuxilioMovilizacion ?? (object)DBNull.Value),
                    new DB2Parameter("@salario_garantizado", x.SalarioGarantizado ?? (object)DBNull.Value),
                    new DB2Parameter("@meses_garantizado", x.MesesGarantizado ?? (object)DBNull.Value),
                    new DB2Parameter("@promedio_variable", x.PromedioVariable ?? (object)DBNull.Value),
                    new DB2Parameter("@requiere_moto", (x.RequiereMoto ? "SI" : "NO")),

                    new DB2Parameter("@correo_gerente_canal", x.CorreoGerenteCanal ?? (object)DBNull.Value),
                    new DB2Parameter("@correo_gerente_division", x.CorreoGerenteDivision ?? (object)DBNull.Value),
                    new DB2Parameter("@area_solicitante", x.AreaSolicitante ?? (object)DBNull.Value),
                    new DB2Parameter("@cargo_jefe_inmediato", x.CargoJefeInmediato ?? (object)DBNull.Value),
                    new DB2Parameter("@centro_costos_f", x.CentroCostosF ?? (object)DBNull.Value),

                    new DB2Parameter("@hora_inicio", x.HoraInicio ?? (object)DBNull.Value),
                    new DB2Parameter("@hora_fin", x.HoraFin ?? (object)DBNull.Value),
                    new DB2Parameter("@activar_proceso_por", x.ActivarProcesoPor ?? (object)DBNull.Value),
                    new DB2Parameter("@persona_reemplaza", x.PersonaReemplaza ?? (object)DBNull.Value),
                    new DB2Parameter("@tipo_jornada", x.TipoJornada ?? (object)DBNull.Value),

                    new DB2Parameter("@estado_req", x.EstadoReq ?? (object)DBNull.Value),
                    new DB2Parameter("@estado", x.Estado ?? (object)DBNull.Value),
                    new DB2Parameter("@nivel_aprobacion", x.NivelAprobacion ?? (object)DBNull.Value),
                    new DB2Parameter("@creado_en", x.CreadoEn ?? (object)DBNull.Value),
                    new DB2Parameter("@fecha_envio_aprobacion", x.FechaEnvioAprobacion ?? (object)DBNull.Value),
                    new DB2Parameter("@fecha_aprobacion", x.FechaAprobacion ?? (object)DBNull.Value),

                    new DB2Parameter("@nombre_candidato", x.NombreCandidato ?? (object)DBNull.Value),
                    new DB2Parameter("@doc_candidato", x.DocCandidato ?? (object)DBNull.Value),
                    new DB2Parameter("@salario_asignado", x.SalarioAsignado ?? (object)DBNull.Value),
                    new DB2Parameter("@fecha_ingreso", x.FechaIngreso ?? (object)DBNull.Value),
                    new DB2Parameter("@aprobaciones_ingreso", x.AprobacionesIngreso ?? (object)DBNull.Value),
                });
                cmd.ExecuteNonQuery();

                using var getId = new DB2Command(
                    "SELECT FIRST 1 id FROM solicitudes_aprobaciones_personal ORDER BY id DESC", cn, tx);
                var idObj = getId.ExecuteScalar();

                tx.Commit();

                var id = idObj is null ? 0 : Convert.ToInt32(idObj);
                _logger.LogInformation("Solicitud insertada correctamente. ID: {Id}, Solicitante: {Solicitante}", id, x.IdSolicitante);
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al insertar solicitud para {Solicitante}", x?.IdSolicitante);
                try { tx.Rollback(); } catch (Exception rbEx) { _logger.LogWarning(rbEx, "Error durante rollback"); }
                return 0;
            }
        }

        public List<SolicitudPersonal> Listar(string usuarioId)
        {
            var list = new List<SolicitudPersonal>();
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                // Sin usuario_solicitante ni correo_vp
                var sql = @"
                SELECT
                  id, id_solicitante, tipo, fecha_solicitud, vicepresidencia, nombre_vp,
                  jefe_inmediato, cargo_requerido, centro_costos, horario_trabajo, dias_laborales, salario_basico,
                  tipo_solicitud, tipo_contrato, meses_contrato, ciudad_trabajo, justificacion, correo_jefe,
                  canal, area, gerente_division, gerente_canal, terr_asignado, cobro_automatico, zona_ciudades,
                  clientes_cargo, canales_cargo, auxilio_movilizacion, salario_garantizado, meses_garantizado,
                  promedio_variable, requiere_moto, correo_gerente_canal, correo_gerente_division, area_solicitante,
                  cargo_jefe_inmediato, centro_costos_f, hora_inicio, hora_fin, activar_proceso_por, persona_reemplaza,
                  tipo_jornada, estado_req, estado, nivel_aprobacion, creado_en, fecha_envio_aprobacion, fecha_aprobacion,
                  nombre_candidato, doc_candidato, salario_asignado, fecha_ingreso, aprobaciones_ingreso
                FROM solicitudes_aprobaciones_personal
                WHERE id_solicitante = @u
                ORDER BY id DESC";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@u", usuarioId));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var o = new SolicitudPersonal
                    {
                        Id = r.GetInt32(0),
                        IdSolicitante = r.IsDBNull(1) ? null : r.GetString(1).Trim(),
                        Tipo = r.IsDBNull(2) ? null : r.GetString(2).Trim(),
                        FechaSolicitud = r.IsDBNull(3) ? null : r.GetString(3).Trim(),
                        Vicepresidencia = r.IsDBNull(4) ? null : r.GetString(4).Trim(),
                        NombreVp = r.IsDBNull(5) ? null : r.GetString(5).Trim(),

                        JefeInmediato = r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                        CargoRequerido = r.IsDBNull(7) ? null : r.GetString(7).Trim(),
                        CentroCostos = r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                        HorarioTrabajo = r.IsDBNull(9) ? null : r.GetString(9).Trim(),
                        DiasLaborales = r.IsDBNull(10) ? null : r.GetString(10).Trim(),
                        SalarioBasico = r.IsDBNull(11) ? null : r.GetString(11).Trim(),
                        TipoSolicitud = r.IsDBNull(12) ? null : r.GetString(12).Trim(),
                        TipoContrato = r.IsDBNull(13) ? null : r.GetString(13).Trim(),
                        MesesContrato = r.IsDBNull(14) ? null : r.GetString(14).Trim(),
                        CiudadTrabajo = r.IsDBNull(15) ? null : r.GetString(15).Trim(),
                        Justificacion = r.IsDBNull(16) ? null : r.GetString(16).Trim(),
                        CorreoJefe = r.IsDBNull(17) ? null : r.GetString(17).Trim(),

                        Canal = r.IsDBNull(18) ? null : r.GetString(18).Trim(),
                        Area = r.IsDBNull(19) ? null : r.GetString(19).Trim(),
                        GerenteDivision = r.IsDBNull(20) ? null : r.GetString(20).Trim(),
                        GerenteCanal = r.IsDBNull(21) ? null : r.GetString(21).Trim(),
                        TerrAsignado = r.IsDBNull(22) ? null : r.GetString(22).Trim(),
                        CobroAutomatico = r.IsDBNull(23) ? null : r.GetString(23).Trim(),
                        ZonaCiudades = r.IsDBNull(24) ? null : r.GetString(24).Trim(),
                        ClientesCargo = r.IsDBNull(25) ? null : r.GetString(25).Trim(),
                        CanalesCargo = r.IsDBNull(26) ? null : r.GetString(26).Trim(),
                        AuxilioMovilizacion = r.IsDBNull(27) ? null : r.GetString(27).Trim(),
                        SalarioGarantizado = r.IsDBNull(28) ? null : r.GetString(28).Trim(),
                        MesesGarantizado = r.IsDBNull(29) ? null : r.GetString(29).Trim(),
                        PromedioVariable = r.IsDBNull(30) ? null : r.GetString(30).Trim(),
                        RequiereMoto = r.IsDBNull(31) ? null : r.GetString(31).Trim(),
                        CorreoGerenteCanal = r.IsDBNull(32) ? null : r.GetString(32).Trim(),
                        CorreoGerenteDivision = r.IsDBNull(33) ? null : r.GetString(33).Trim(),

                        AreaSolicitante = r.IsDBNull(34) ? null : r.GetString(34).Trim(),
                        CargoJefeInmediato = r.IsDBNull(35) ? null : r.GetString(35).Trim(),
                        CentroCostosF = r.IsDBNull(36) ? null : r.GetString(36).Trim(),
                        HoraInicio = r.IsDBNull(37) ? null : r.GetString(37).Trim(),
                        HoraFin = r.IsDBNull(38) ? null : r.GetString(38).Trim(),
                        ActivarProcesoPor = r.IsDBNull(39) ? null : r.GetString(39).Trim(),
                        PersonaReemplaza = r.IsDBNull(40) ? null : r.GetString(40).Trim(),
                        TipoJornada = r.IsDBNull(41) ? null : r.GetString(41).Trim(),

                        EstadoReq = r.IsDBNull(42) ? null : r.GetString(42).Trim(),
                        Estado = r.IsDBNull(43) ? null : r.GetString(43).Trim(),
                        NivelAprobacion = r.IsDBNull(44) ? null : r.GetString(44).Trim(),
                        CreadoEn = r.IsDBNull(45) ? null : r.GetString(45).Trim(),
                        FechaEnvioAprobacion = r.IsDBNull(46) ? null : r.GetString(46).Trim(),
                        FechaAprobacion = r.IsDBNull(47) ? null : r.GetString(47).Trim(),

                        NombreCandidato = r.IsDBNull(48) ? null : r.GetString(48).Trim(),
                        DocCandidato = r.IsDBNull(49) ? null : r.GetString(49).Trim(),
                        SalarioAsignado = r.IsDBNull(50) ? null : r.GetString(50).Trim(),
                        FechaIngreso = r.IsDBNull(51) ? null : r.GetString(51).Trim(),
                        AprobacionesIngreso = r.IsDBNull(52) ? null : r.GetString(52).Trim()
                    };
                    list.Add(o);
                }

                _logger.LogInformation("Listar: usuario {UsuarioId} → {Total} registros", usuarioId, list.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar solicitudes para usuario {UsuarioId}", usuarioId);
            }

            return list;
        }

        public List<string> ListarCanales()
        {
            var canales = new List<string>();

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                const string sql = @"
                    SELECT DISTINCT TRIM(canal) AS canal
                    FROM cargos_canales_requisicio_personal
                    WHERE canal IS NOT NULL AND TRIM(canal) <> ''
                    ORDER BY canal";

                using var cmd = new DB2Command(sql, cn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var canal = r.IsDBNull(0) ? null : r.GetString(0).Trim();
                    if (!string.IsNullOrWhiteSpace(canal))
                        canales.Add(canal);
                }

                Console.WriteLine($"[CanalesService] ListarCanales => {canales.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CanalesService] Error ListarCanales: {ex}");
            }

            return canales;
        }

        public List<CargoCanal> ListarCargosCanales(string? canal = null)
        {
            var list = new List<CargoCanal>();

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT id, TRIM(cargo) AS cargo, TRIM(canal) AS canal
                    FROM cargos_canales_requisicio_personal
                    /**where**/
                    ORDER BY canal, cargo";

                var where = "";
                using var cmd = new DB2Command();
                cmd.Connection = cn;

                if (!string.IsNullOrWhiteSpace(canal))
                {
                    where = "WHERE TRIM(canal) = @canal";
                    cmd.Parameters.Add(new DB2Parameter("@canal", canal.Trim()));
                }

                cmd.CommandText = sql.Replace("/**where**/", where);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var item = new CargoCanal
                    {
                        Id = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                        Cargo = r.IsDBNull(1) ? null : r.GetString(1).Trim(),
                        Canal = r.IsDBNull(2) ? null : r.GetString(2).Trim()
                    };
                    list.Add(item);
                }

                Console.WriteLine($"[CargosCanalesRelService] Listar => {list.Count} (filtro canal: {(canal ?? "(none)")})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CargosCanalesRelService] Error Listar: {ex}");
            }

            return list;
        }

        public bool AplicarAccion(int id, string estado, string? motivo = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(estado)) return false;

                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = @estado,
                       fecha_aprobacion = CURRENT
                 WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@estado", estado));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                var n = cmd.ExecuteNonQuery();
                _logger.LogInformation("AplicarAccion: id={Id}, estado={Estado}, updated={Rows}", id, estado, n);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar acción {Estado} para id {Id}", estado, id);
                return false;
            }
        }
    }
}
