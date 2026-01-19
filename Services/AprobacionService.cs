using System;
using System.Collections.Generic;
using BackendRequisicionPersonal.Models;
using IBM.Data.Db2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Services
{
    public class AprobacionService
    {
        private readonly string _connectionString;
        private readonly ILogger<AprobacionService> _logger;
        private readonly IConfiguration _config;

        public AprobacionService(
            IConfiguration configuration,
            ILogger<AprobacionService> logger,
            string connectionString)
        {
            _config = configuration;
            _logger = logger;
            _connectionString = connectionString;
        }

        public bool AplicarAccion(
            int id,
            string estado,
            string? motivo = null,
            string? actorEmail = null,
            string? actorNombre = null)
        {
            try
            {
                if (id <= 0 || string.IsNullOrWhiteSpace(estado)) return false;

                static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

                var up = Norm(estado);

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["APROBADO POR GESTION GH"] = "APROBADO_GH",
                    ["APROBADO POR GESTIÓN GH"] = "APROBADO_GH",
                    ["RECHAZADO POR GESTION GH"] = "RECHAZADO_GH",
                    ["RECHAZADO POR GESTIÓN GH"] = "RECHAZADO_GH",
                    ["APROBADA"] = "APROBADA_NIVEL",
                    ["APROBADO"] = "APROBADA_NIVEL",
                    ["RECHAZADA"] = "RECHAZADA_NIVEL",
                    ["RECHAZADO"] = "RECHAZADA_NIVEL",
                    ["EN SELECCION"] = "EN_SELECCION",
                    ["APROBADO POR SELECCION"] = "APROBADO_SEL",
                    ["APROBADO POR SELECCIÓN"] = "APROBADO_SEL",
                    ["RECHAZADO POR SELECCION"] = "RECHAZADO_SEL",
                    ["RECHAZADO POR SELECCIÓN"] = "RECHAZADO_SEL",
                    ["APROBADO POR VP GH"] = "APROBADO_VPGH",
                    ["RECHAZADO POR VP GH"] = "RECHAZADO_VPGH",
                    ["CERRADO"] = "CERRADO"
                };

                if (!map.TryGetValue(up, out var accion))
                    return false;

                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                using var tx = cn.BeginTransaction();

                var (nivelActual, estadoGeneral, ap1Correo, ap1Estado, ap2Correo, ap2Estado, ap3Correo, ap3Estado) 
                    = ObtenerEstadoActual(id, cn, tx);

                if (nivelActual == null) return false;

                string CalcularPrimerPendiente()
                {
                    if (!string.IsNullOrEmpty(ap1Correo) && (string.IsNullOrEmpty(ap1Estado) || ap1Estado == "PENDIENTE"))
                        return "1";
                    if (!string.IsNullOrEmpty(ap2Correo) && (string.IsNullOrEmpty(ap2Estado) || ap2Estado == "PENDIENTE"))
                        return "2";
                    if (!string.IsNullOrEmpty(ap3Correo) && (string.IsNullOrEmpty(ap3Estado) || ap3Estado == "PENDIENTE"))
                        return "3";
                    return "FINAL";
                }

                if (nivelActual == "FINAL")
                    nivelActual = CalcularPrimerPendiente();

                switch (accion)
                {
                    case "EN_SELECCION":
                        return ProcesarCambioEstadoGeneral(id, cn, tx, "EN SELECCION", actorNombre, actorEmail, motivo);

                    case "APROBADO_GH":
                        return ProcesarAprobadoGH(id, cn, tx, estadoGeneral, CalcularPrimerPendiente, ap2Correo, ap3Correo);

                    case "RECHAZADO_GH":
                        return ProcesarRechazadoGH(id, cn, tx, actorNombre, actorEmail, motivo);

                    case "APROBADO_SEL":
                        return ProcesarAprobadoSeleccion(id, cn, tx, estadoGeneral);

                    case "RECHAZADO_SEL":
                        return ProcesarRechazadoSeleccion(id, cn, tx);

                    case "APROBADO_VPGH":
                        return ProcesarAprobadoVpGh(id, cn, tx, estadoGeneral);

                    case "RECHAZADO_VPGH":
                        return ProcesarRechazadoVpGh(id, cn, tx, actorNombre, actorEmail, motivo);

                    case "APROBADA_NIVEL":
                    case "RECHAZADA_NIVEL":
                        return ProcesarAccionNivel(id, cn, tx, accion, nivelActual, motivo, actorEmail, actorNombre, 
                            ap2Correo, ap3Correo, estadoGeneral);

                    default:
                        tx.Commit();
                        return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar acción {Estado} para id {Id}", estado, id);
                return false;
            }
        }

        private bool ProcesarCambioEstadoGeneral(
            int id,
            DB2Connection cn,
            DB2Transaction tx,
            string nuevoEstado,
            string? actorNombre,
            string? actorEmail,
            string? motivo)
        {
            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = @estado,
                       vp_cierre_motivo = @motivo,
                       vp_cierre_nombre = @actorNombre,
                       vp_cierre_correo = @actorEmail
                 WHERE id = @id";

            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@estado", nuevoEstado));
            cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@id", id));

            var n = cmd.ExecuteNonQuery();
            _logger.LogInformation("CambioEstadoGeneral: id={Id}, estado={Estado}, rows={Rows}", id, nuevoEstado, n);

            tx.Commit();
            return n > 0;
        }

        public bool MarcarRevisadoRrhh(int id, string? identificacion = null)
        {
            if (id <= 0) return false;

            string ghNombre = string.Empty;
            string ghCorreo = string.Empty;

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                if (!string.IsNullOrWhiteSpace(identificacion))
                {
                    const string sqlPerfil = @"
                        SELECT FIRST 1 
                            jefe_inmediato_solicitante, 
                            e_mail 
                        FROM requisiciones_aprobaciones_personal 
                        WHERE identificacion = @identificacion";

                    using var cmdPerfil = new DB2Command(sqlPerfil, cn);
                    cmdPerfil.Parameters.Add(new DB2Parameter("@identificacion", identificacion));

                    using var rd = cmdPerfil.ExecuteReader();
                    if (rd.Read())
                    {
                        ghNombre = rd["jefe_inmediato_solicitante"]?.ToString()?.Trim() ?? "";
                        ghCorreo = rd["e_mail"]?.ToString()?.Trim() ?? "";
                    }
                }

                if (string.IsNullOrWhiteSpace(ghNombre))
                    ghNombre = "GESTIÓN HUMANA";

                if (string.IsNullOrWhiteSpace(ghCorreo))
                    ghCorreo = _config.GetSection("RRHH:CorreosRevision").Get<string[]>()?[0];

                if (string.IsNullOrWhiteSpace(identificacion))
                    identificacion = _config["RRHH:Identificacion"];

                const string sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET fecha_envio_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           gh_rev_nombre           = @ghNombre,
                           gh_rev_correo           = @ghCorreo,
                           gh_rev_fecha            = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           estado                  = 'EN APROBACION'
                     WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@ghNombre", ghNombre));
                cmd.Parameters.Add(new DB2Parameter("@ghCorreo", ghCorreo));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                int n = cmd.ExecuteNonQuery();

                _logger.LogInformation("MarcarRevisadoRrhh OK id={Id}, rows={Rows}, GH={Nombre}<{Correo}>", 
                    id, n, ghNombre, ghCorreo);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en MarcarRevisadoRrhh id={Id}, identificacion={Identificacion}", 
                    id, identificacion);
                return false;
            }
        }

        public bool ActualizarEstadoEnVpGh(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                
                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado = 'EN VP GH'
                     WHERE id = @id";
                
                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                var n = cmd.ExecuteNonQuery();
                
                _logger.LogInformation("ActualizarEstadoEnVpGh: id={Id}, rows={Rows}", id, n);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ActualizarEstadoEnVpGh id={Id}", id);
                return false;
            }
        }

        public bool CerrarRequisicion(int id, string? identificacion = null)
        {
            if (id <= 0) return false;

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                identificacion = !string.IsNullOrWhiteSpace(identificacion)
                    ? identificacion.Trim()
                    : _config["VPGH:Identificacion"];

                if (string.IsNullOrWhiteSpace(identificacion))
                {
                    _logger.LogWarning("?? Sin Identificación (ni parámetro ni VPGH:Identificacion en appsettings).");
                    return false;
                }

                string vpNombre = "";
                string vpCorreo = "";

                const string sqlPerfil = @"
                    SELECT FIRST 1 
                           jefe_inmediato_solicitante, 
                           e_mail
                      FROM requisiciones_aprobaciones_personal
                     WHERE identificacion = @identificacion";

                using (var cmdPerfil = new DB2Command(sqlPerfil, cn))
                {
                    cmdPerfil.Parameters.Add(new DB2Parameter("@identificacion", identificacion));
                    using var rd = cmdPerfil.ExecuteReader();
                    if (rd.Read())
                    {
                        vpNombre = rd["jefe_inmediato_solicitante"]?.ToString()?.Trim() ?? "";
                        vpCorreo = rd["e_mail"]?.ToString()?.Trim() ?? "";
                    }
                }

                if (string.IsNullOrWhiteSpace(vpNombre)) vpNombre = "VP GESTION HUMANA";
                if (string.IsNullOrWhiteSpace(vpCorreo)) vpCorreo = "seleccion@recamier.com";

                const string sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado           = 'CERRADO',
                           nivel_aprobacion = 'FINAL',
                           fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           vp_cierre_nombre = @vpNombre,
                           vp_cierre_correo = @vpCorreo,
                           fecha_cierre     = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                     WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@vpNombre", vpNombre));
                cmd.Parameters.Add(new DB2Parameter("@vpCorreo", vpCorreo));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                int n = cmd.ExecuteNonQuery();
                _logger.LogInformation("? CerrarRequisicion: id={Id}, rows={Rows}, VP={Nombre}<{Correo}>", 
                    id, n, vpNombre, vpCorreo);

                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en CerrarRequisicion id={Id}, identificacion={Identificacion}", 
                    id, identificacion);
                return false;
            }
        }

        public bool AprobarNomina(int id, string? actorNombre, string? actorEmail)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET nomina_rev_estado = 'APROBADA',
                           nomina_rev_nombre = @actorNombre,
                           nomina_rev_correo = @actorEmail,
                           nomina_rev_fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           estado            = 'EN VP GH'
                     WHERE id = @id
                       AND UPPER(TRIM(estado)) = 'EN NOMINA'";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                int n = cmd.ExecuteNonQuery();
                _logger.LogInformation("AprobarNomina: id={Id}, rows={Rows}, actor={Actor}", id, n, actorNombre);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en AprobarNomina id={Id}", id);
                return false;
            }
        }

        public bool RechazarNomina(int id, string? actorNombre, string? actorEmail, string? motivo)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET nomina_rev_estado = 'RECHAZADA',
                           nomina_rev_nombre = @actorNombre,
                           nomina_rev_correo = @actorEmail,
                           nomina_rev_fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           nomina_rev_motivo = @motivo,
                           estado            = 'RECHAZADO POR NOMINA',
                           nivel_aprobacion  = 'FINAL'
                     WHERE id = @id
                       AND UPPER(TRIM(estado)) = 'EN NOMINA'";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                int n = cmd.ExecuteNonQuery();
                _logger.LogInformation("RechazarNomina: id={Id}, rows={Rows}, motivo={Motivo}", id, n, motivo);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en RechazarNomina id={Id}", id);
                return false;
            }
        }

        public bool ReenviarAVpGhAEnNomina(int id, string? actorNombre, string? actorEmail, string? motivo)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado = 'EN NOMINA',
                           vp_cierre_motivo = @motivo,
                           vp_cierre_nombre = @actorNombre,
                           vp_cierre_correo = @actorEmail,
                           fecha_cierre = NULL
                     WHERE id = @id
                       AND UPPER(TRIM(estado)) = 'EN VP GH'";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                int n = cmd.ExecuteNonQuery();
                _logger.LogInformation("ReenviarAVpGhAEnNomina: id={Id}, rows={Rows}, actor={Actor}", id, n, actorNombre);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ReenviarAVpGhAEnNomina id={Id}", id);
                return false;
            }
        }

        private (string? nivelActual, string estadoGeneral, string? ap1Correo, string? ap1Estado, 
                 string? ap2Correo, string? ap2Estado, string? ap3Correo, string? ap3Estado) 
            ObtenerEstadoActual(int id, DB2Connection cn, DB2Transaction tx)
        {
            static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var selSql = @"
                SELECT
                    nivel_aprobacion, estado,
                    ap1_correo, ap1_estado,
                    ap2_correo, ap2_estado,
                    ap3_correo, ap3_estado
                FROM solicitudes_aprobaciones_personal
                WHERE id = @id";

            using var sel = new DB2Command(selSql, cn, tx);
            sel.Parameters.Add(new DB2Parameter("@id", id));
            using var r = sel.ExecuteReader();
            
            if (!r.Read()) 
                return (null, "", null, null, null, null, null, null);

            return (
                r.IsDBNull(0) ? "FINAL" : Norm(r.GetString(0)),
                r.IsDBNull(1) ? "EN APROBACION" : Norm(r.GetString(1)),
                r.IsDBNull(2) ? null : r.GetString(2).Trim(),
                r.IsDBNull(3) ? null : Norm(r.GetString(3)),
                r.IsDBNull(4) ? null : r.GetString(4).Trim(),
                r.IsDBNull(5) ? null : Norm(r.GetString(5)),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : Norm(r.GetString(7))
            );
        }

        private bool ProcesarAprobadoGH(int id, DB2Connection cn, DB2Transaction tx, string estadoGeneral, 
            Func<string> calcularPrimerPendiente, string? ap2Correo, string? ap3Correo)
        {
            if (estadoGeneral != "EN REVISION POR GESTION GH" && estadoGeneral != "EN REVISIÓN POR GESTIÓN GH")
            {
                tx.Commit();
                return true;
            }

            string primerNivel = calcularPrimerPendiente();
            
            if (primerNivel == "FINAL")
            {
                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado = 'EN VP GH',
                           nivel_aprobacion = 'FINAL',
                           fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                     WHERE id = @id";
                
                using var cmd = new DB2Command(sql, cn, tx);
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                cmd.ExecuteNonQuery();
            }
            else
            {
                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado = 'EN APROBACION',
                           nivel_aprobacion = @nivel
                     WHERE id = @id";
                
                using var cmd = new DB2Command(sql, cn, tx);
                cmd.Parameters.Add(new DB2Parameter("@nivel", primerNivel));
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        private bool ProcesarRechazadoGH(int id, DB2Connection cn, DB2Transaction tx, 
            string? actorNombre, string? actorEmail, string? motivo)
        {
            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'RECHAZADO POR GESTION GH',
                       nivel_aprobacion = 'FINAL',
                       gh_rev_fecha = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                       gh_rev_nombre = @actorNombre,
                       gh_rev_correo = @actorEmail,
                       gh_rev_motivo = @motivo,
                       ap1_estado = 'NA',
                       ap2_estado = 'NA',
                       ap3_estado = 'NA'
                 WHERE id = @id";

            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@id", id));
            cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
            cmd.ExecuteNonQuery();

            tx.Commit();
            return true;
        }

        private bool ProcesarAprobadoSeleccion(int id, DB2Connection cn, DB2Transaction tx, string estadoGeneral)
        {
            if (estadoGeneral != "EN SELECCION" && estadoGeneral != "EN SELECCIÓN")
            {
                tx.Commit();
                return true;
            }

            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'EN VP GH'
                 WHERE id = @id";
            
            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@id", id));
            cmd.ExecuteNonQuery();

            tx.Commit();
            return true;
        }

        private bool ProcesarRechazadoSeleccion(int id, DB2Connection cn, DB2Transaction tx)
        {
            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'RECHAZADO POR SELECCION',
                       nivel_aprobacion = 'FINAL',
                       fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                 WHERE id = @id";
            
            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@id", id));
            cmd.ExecuteNonQuery();

            tx.Commit();
            return true;
        }

        private bool ProcesarAprobadoVpGh(int id, DB2Connection cn, DB2Transaction tx, string estadoGeneral)
        {
            if (estadoGeneral != "EN VP GH")
            {
                tx.Commit();
                return true;
            }

            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'CERRADO',
                       nivel_aprobacion = 'FINAL',
                       fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                 WHERE id = @id";
            
            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@id", id));
            cmd.ExecuteNonQuery();

            tx.Commit();
            return true;
        }

        private bool ProcesarRechazadoVpGh(int id, DB2Connection cn, DB2Transaction tx, 
            string? actorNombre, string? actorEmail, string? motivo)
        {
            var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado           = 'RECHAZADO POR VP GH',
                       nivel_aprobacion = 'FINAL',
                       fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                       vp_cierre_nombre = @actorNombre,
                       vp_cierre_correo = @actorEmail,
                       vp_cierre_motivo = @motivo,
                       fecha_cierre     = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                 WHERE id = @id";

            using var cmd = new DB2Command(sql, cn, tx);
            cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@actorEmail", (object?)actorEmail ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
            cmd.Parameters.Add(new DB2Parameter("@id", id));
            cmd.ExecuteNonQuery();

            tx.Commit();
            return true;
        }

        private bool ProcesarAccionNivel(int id, DB2Connection cn, DB2Transaction tx, string accion, 
            string nivelActual, string? motivo, string? actorEmail, string? actorNombre,
            string? ap2Correo, string? ap3Correo, string estadoGeneral)
        {
            if (estadoGeneral != "EN APROBACION" && estadoGeneral != "EN APROBACIÓN")
            {
                tx.Commit();
                return true;
            }

            int NivelToInt(string s) => s switch { "1" => 1, "2" => 2, "3" => 3, _ => 0 };
            
            string NextNivelCalc(string curr)
            {
                return curr switch
                {
                    "1" => !string.IsNullOrWhiteSpace(ap2Correo) ? "2" :
                           (!string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL"),
                    "2" => !string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL",
                    _ => "FINAL"
                };
            }

            int lvl = Math.Max(1, NivelToInt(nivelActual));
            string prefix = $"ap{lvl}_";
            
            string setCorreoYNombre = "";
            if (!string.IsNullOrWhiteSpace(actorEmail))
            {
                setCorreoYNombre = $@",
                   {prefix}correo = CASE WHEN {prefix}correo IS NULL OR TRIM({prefix}correo) = ''
                                         THEN CAST(@actorEmail AS CHAR(100)) ELSE {prefix}correo END,
                   {prefix}nombre = CASE WHEN {prefix}nombre IS NULL OR TRIM({prefix}nombre) = ''
                                         THEN CAST(@actorNombre AS CHAR(100)) ELSE {prefix}nombre END";
            }

            if (accion == "RECHAZADA_NIVEL")
            {
                var sql = $@"
                    UPDATE solicitudes_aprobaciones_personal
                       SET {prefix}estado = 'RECHAZADA',
                           {prefix}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           {prefix}motivo = @motivo
                           {setCorreoYNombre},
                           estado = 'RECHAZADA',
                           nivel_aprobacion = 'FINAL',
                           fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                     WHERE id = @id";
                
                using var cmd = new DB2Command(sql, cn, tx);
                cmd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                if (!string.IsNullOrWhiteSpace(actorEmail))
                {
                    cmd.Parameters.Add(new DB2Parameter("@actorEmail", actorEmail.Trim()));
                    cmd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                }
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                cmd.ExecuteNonQuery();

                tx.Commit();
                return true;
            }

            if (accion == "APROBADA_NIVEL")
            {
                string nextNivel = NextNivelCalc(nivelActual);
                bool finalizaCadena = (nextNivel == "FINAL");

                var updSql = $@"
                    UPDATE solicitudes_aprobaciones_personal
                       SET {prefix}estado = 'APROBADA',
                           {prefix}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           {prefix}motivo = @motivo
                           {setCorreoYNombre},
                           estado = CASE WHEN @finaliza = 1 THEN 'EN SELECCION' ELSE 'EN APROBACION' END,
                           nivel_aprobacion = CASE WHEN @finaliza = 1 THEN 'FINAL' ELSE @nextNivel END,
                           fecha_aprobacion = CASE WHEN @finaliza = 1 THEN TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                                                   ELSE fecha_aprobacion END
                     WHERE id = @id";

                using var upd = new DB2Command(updSql, cn, tx);
                upd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                if (!string.IsNullOrWhiteSpace(actorEmail))
                {
                    upd.Parameters.Add(new DB2Parameter("@actorEmail", actorEmail.Trim()));
                    upd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                }
                upd.Parameters.Add(new DB2Parameter("@finaliza", finalizaCadena ? 1 : 0));
                upd.Parameters.Add(new DB2Parameter("@nextNivel", nextNivel));
                upd.Parameters.Add(new DB2Parameter("@id", id));
                upd.ExecuteNonQuery();

                tx.Commit();
                return true;
            }

            tx.Commit();
            return true;
        }
    }
}
