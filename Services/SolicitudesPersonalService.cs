using System;
using System.Collections.Generic;
using System.Text;
using BackendRequisicionPersonal.Models; // DTOs
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
        private readonly IConfiguration _config;

        public SolicitudesPersonalService(
            IConfiguration configuration,
            IWebHostEnvironment env,
            ILogger<SolicitudesPersonalService> logger)
        {
            _logger = logger;
            _config = configuration;
            _connectionString = configuration.GetConnectionString("InformixConnection");
            _logger.LogInformation("Cadena de conexión utilizada: InformixConnection");
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
                string tipoNorm = (x.Tipo ?? "").Trim().ToUpperInvariant();

                // ===== Aprobadores según tipo =====
                string? ap1Nombre, ap1Correo, ap2Nombre, ap2Correo, ap3Nombre, ap3Correo;

                if (tipoNorm == "ADMINISTRATIVO")
                {
                    // Un (1) aprobador
                    ap1Nombre = !string.IsNullOrWhiteSpace(x.GerenteDivision) ? x.GerenteDivision?.Trim()
                              : !string.IsNullOrWhiteSpace(x.GerenteCanal) ? x.GerenteCanal?.Trim()
                              : x.JefeInmediato?.Trim();

                    ap1Correo = !string.IsNullOrWhiteSpace(x.CorreoGerenteDivision) ? x.CorreoGerenteDivision?.Trim()
                              : !string.IsNullOrWhiteSpace(x.CorreoGerenteCanal) ? x.CorreoGerenteCanal?.Trim()
                              : x.CorreoJefe?.Trim();

                    ap2Nombre = ap2Correo = ap3Nombre = ap3Correo = null;
                }
                else
                {
                    // Comercial: GC -> GD. (VP GH NO es parte de esta cadena; va en etapa propia EN VP GH)
                    ap1Nombre = x.GerenteCanal?.Trim();
                    ap1Correo = x.CorreoGerenteCanal?.Trim();

                    ap2Nombre = x.GerenteDivision?.Trim();
                    ap2Correo = x.CorreoGerenteDivision?.Trim();

                    ap3Nombre = null;
                    ap3Correo = null;
                }

                string? ap1Estado = string.IsNullOrEmpty(ap1Correo) ? null : "PENDIENTE";
                string? ap2Estado = string.IsNullOrEmpty(ap2Correo) ? null : "PENDIENTE";
                string? ap3Estado = string.IsNullOrEmpty(ap3Correo) ? null : "PENDIENTE";

                string estado = "EN REVISION POR GESTION GH";

                string nivel = "FINAL";
                if (!string.IsNullOrEmpty(ap1Correo)) nivel = "1";
                else if (!string.IsNullOrEmpty(ap2Correo)) nivel = "2";
                else if (!string.IsNullOrEmpty(ap3Correo)) nivel = "3";

                string creadoEn = string.IsNullOrWhiteSpace(x.CreadoEn)
                    ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    : x.CreadoEn;

                string? fechaEnvio = null;
                string? fechaAprob = null;

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
                  estado, nivel_aprobacion, creado_en, fecha_envio_aprobacion, fecha_aprobacion,
                  salario_asignado, fecha_ingreso, aprobaciones_ingreso,
                  ap1_nombre, ap1_correo, ap1_estado, ap1_fecha, ap1_motivo,
                  ap2_nombre, ap2_correo, ap2_estado, ap2_fecha, ap2_motivo,
                  ap3_nombre, ap3_correo, ap3_estado, ap3_fecha, ap3_motivo
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
                  @estado, @nivel_aprobacion, @creado_en, @fecha_envio_aprobacion, @fecha_aprobacion,
                  @salario_asignado, @fecha_ingreso, @aprobaciones_ingreso,
                  @ap1_nombre, @ap1_correo, @ap1_estado, NULL, NULL,
                  @ap2_nombre, @ap2_correo, @ap2_estado, NULL, NULL,
                  @ap3_nombre, @ap3_correo, @ap3_estado, NULL, NULL
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
                    new DB2Parameter("@horario_trabajo", (object?)x.HorarioTrabajo ?? DBNull.Value),
                    new DB2Parameter("@dias_laborales", x.DiasLaborales ?? (object)DBNull.Value),

                    new DB2Parameter("@salario_basico", x.SalarioBasico ?? (object)DBNull.Value),
                    new DB2Parameter("@tipo_solicitud", x.TipoSolicitud ?? (object)DBNull.Value),
                    new DB2Parameter("@tipo_contrato", x.TipoContrato ?? (object)DBNull.Value),
                    new DB2Parameter("@meses_contrato", (object?)x.MesesContrato ?? DBNull.Value),
                    new DB2Parameter("@ciudad_trabajo", x.CiudadTrabajo ?? (object)DBNull.Value),

                    new DB2Parameter("@justificacion", x.Justificacion ?? (object)DBNull.Value),
                    new DB2Parameter("@correo_jefe", x.CorreoJefe ?? (object)DBNull.Value),
                    new DB2Parameter("@canal", (object?)x.Canal ?? DBNull.Value),
                    new DB2Parameter("@area", (object?)x.Area ?? DBNull.Value),
                    new DB2Parameter("@gerente_division", (object?)x.GerenteDivision ?? DBNull.Value),
                    new DB2Parameter("@gerente_canal", (object?)x.GerenteCanal ?? DBNull.Value),

                    new DB2Parameter("@terr_asignado", (object?)x.TerrAsignado ?? DBNull.Value),
                    new DB2Parameter("@cobro_automatico", (object?)x.CobroAutomatico ?? DBNull.Value),
                    new DB2Parameter("@zona_ciudades", (object?)x.ZonaCiudades ?? DBNull.Value),
                    new DB2Parameter("@clientes_cargo", (object?)x.ClientesCargo ?? DBNull.Value),
                    new DB2Parameter("@canales_cargo", (object?)x.CanalesCargo ?? DBNull.Value),

                    new DB2Parameter("@auxilio_movilizacion", (object?)x.AuxilioMovilizacion ?? DBNull.Value),
                    new DB2Parameter("@salario_garantizado", (object?)x.SalarioGarantizado ?? DBNull.Value),
                    new DB2Parameter("@meses_garantizado", (object?)x.MesesGarantizado ?? DBNull.Value),
                    new DB2Parameter("@promedio_variable", (object?)x.PromedioVariable ?? DBNull.Value),

                    new DB2Parameter("@requiere_moto", (x.RequiereMoto ? "SI" : "NO")),

                    new DB2Parameter("@correo_gerente_canal", (object?)x.CorreoGerenteCanal ?? DBNull.Value),
                    new DB2Parameter("@correo_gerente_division", (object?)x.CorreoGerenteDivision ?? DBNull.Value),
                    new DB2Parameter("@area_solicitante", (object?)x.AreaSolicitante ?? DBNull.Value),
                    new DB2Parameter("@cargo_jefe_inmediato", (object?)x.CargoJefeInmediato ?? DBNull.Value),
                    new DB2Parameter("@centro_costos_f", (object?)x.CentroCostosF ?? DBNull.Value),

                    new DB2Parameter("@hora_inicio", (object?)x.HoraInicio ?? DBNull.Value),
                    new DB2Parameter("@hora_fin", (object?)x.HoraFin ?? DBNull.Value),
                    new DB2Parameter("@activar_proceso_por", (object?)x.ActivarProcesoPor ?? DBNull.Value),
                    new DB2Parameter("@persona_reemplaza", (object?)x.PersonaReemplaza ?? DBNull.Value),
                    new DB2Parameter("@tipo_jornada", (object?)x.TipoJornada ?? DBNull.Value),

                    new DB2Parameter("@estado", estado),
                    new DB2Parameter("@nivel_aprobacion", nivel),
                    new DB2Parameter("@creado_en", creadoEn),
                    new DB2Parameter("@fecha_envio_aprobacion", (object?)fechaEnvio ?? DBNull.Value),
                    new DB2Parameter("@fecha_aprobacion", (object?)fechaAprob ?? DBNull.Value),

                    new DB2Parameter("@salario_asignado", (object?)x.SalarioAsignado ?? DBNull.Value),
                    new DB2Parameter("@fecha_ingreso", (object?)x.FechaIngreso ?? DBNull.Value),
                    new DB2Parameter("@aprobaciones_ingreso", (object?)x.AprobacionesIngreso ?? DBNull.Value),

                    new DB2Parameter("@ap1_nombre", (object?)ap1Nombre ?? DBNull.Value),
                    new DB2Parameter("@ap1_correo", (object?)ap1Correo ?? DBNull.Value),
                    new DB2Parameter("@ap1_estado", (object?)ap1Estado ?? DBNull.Value),

                    new DB2Parameter("@ap2_nombre", (object?)ap2Nombre ?? DBNull.Value),
                    new DB2Parameter("@ap2_correo", (object?)ap2Correo ?? DBNull.Value),
                    new DB2Parameter("@ap2_estado", (object?)ap2Estado ?? DBNull.Value),

                    new DB2Parameter("@ap3_nombre", (object?)ap3Nombre ?? DBNull.Value),
                    new DB2Parameter("@ap3_correo", (object?)ap3Correo ?? DBNull.Value),
                    new DB2Parameter("@ap3_estado", (object?)ap3Estado ?? DBNull.Value),
                });
                cmd.ExecuteNonQuery();

                using var getId = new DB2Command("SELECT FIRST 1 id FROM solicitudes_aprobaciones_personal ORDER BY id DESC", cn, tx);
                var idObj = getId.ExecuteScalar();

                tx.Commit();
                var id = idObj is null ? 0 : Convert.ToInt32(idObj);
                _logger.LogInformation("Insert OK. ID: {Id} (Tipo={Tipo}, NivelInicial={Nivel})", id, x.Tipo, nivel);
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al insertar solicitud");
                try { tx.Rollback(); } catch (Exception rbEx) { _logger.LogWarning(rbEx, "Rollback error"); }
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

                var sql = @"
                SELECT
                  id,
                  id_solicitante, tipo, fecha_solicitud, vicepresidencia, nombre_vp,
                  jefe_inmediato, cargo_requerido, centro_costos, horario_trabajo, dias_laborales, salario_basico,
                  tipo_solicitud, tipo_contrato, meses_contrato, ciudad_trabajo, justificacion, correo_jefe,
                  canal, area, gerente_division, gerente_canal, terr_asignado, cobro_automatico, zona_ciudades,
                  clientes_cargo, canales_cargo, auxilio_movilizacion, salario_garantizado, meses_garantizado,
                  promedio_variable, requiere_moto, correo_gerente_canal, correo_gerente_division, area_solicitante,
                  cargo_jefe_inmediato, centro_costos_f, hora_inicio, hora_fin, activar_proceso_por, persona_reemplaza,
                  tipo_jornada,
                  estado, nivel_aprobacion, creado_en, fecha_envio_aprobacion, fecha_aprobacion,
                  salario_asignado, fecha_ingreso, aprobaciones_ingreso,
                  nombre_seleccionado, identificacion_seleccionado, fecha_ingreso_seleccionado, tipo_contrato_seleccionado
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

                        Estado = r.IsDBNull(42) ? null : r.GetString(42).Trim(),
                        NivelAprobacion = r.IsDBNull(43) ? null : r.GetString(43).Trim(),
                        CreadoEn = r.IsDBNull(44) ? null : r.GetString(44).Trim(),
                        FechaEnvioAprobacion = r.IsDBNull(45) ? null : r.GetString(45).Trim(),
                        FechaAprobacion = r.IsDBNull(46) ? null : r.GetString(46).Trim(),

                        SalarioAsignado = r.IsDBNull(47) ? null : r.GetString(47).Trim(),
                        FechaIngreso = r.IsDBNull(48) ? null : r.GetString(48).Trim(),
                        AprobacionesIngreso = r.IsDBNull(49) ? null : r.GetString(49).Trim(),

                        NombreSeleccionado = r.IsDBNull(50) ? null : r.GetString(50).Trim(),
                        IdentificacionSeleccionado = r.IsDBNull(51) ? null : r.GetString(51).Trim(),
                        FechaIngresoSeleccionado = r.IsDBNull(52) ? null : r.GetString(52).Trim(),
                        TipoContratoSeleccionado = r.IsDBNull(53) ? null : r.GetString(53).Trim(),
                    };
                    list.Add(o);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar solicitudes para usuario {UsuarioId}", usuarioId);
            }
            return list;
        }

        public List<CargoCanal> ListarCargosCanales(string? canal = null)
        {
            var list = new List<CargoCanal>();
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
            SELECT id, TRIM(cargo) AS cargo, TRIM(centro_costos) AS centro_costos, TRIM(canal) AS canal
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
                        CentroCostos = r.IsDBNull(2) ? null : r.GetString(2).Trim(),
                        Canal = r.IsDBNull(3) ? null : r.GetString(3).Trim()
                    };
                    list.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ListarCargosCanales");
            }
            return list;
        }

        public List<dynamic> ListarCargosAdministrativos(string? canal, string? area)
        {
            var result = new List<dynamic>();
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
            SELECT id,
                   TRIM(cargo) AS cargo
            FROM cargos_canales_requisicio_personal_administrativo
            /**where**/
            ORDER BY cargo";

                var whereParts = new List<string>();
                using var cmd = new DB2Command();
                cmd.Connection = cn;

                if (!string.IsNullOrWhiteSpace(canal))
                {
                    whereParts.Add("UPPER(TRIM(canal)) = UPPER(@canal)");
                    cmd.Parameters.Add(new DB2Parameter("@canal", canal.Trim()));
                }

                if (!string.IsNullOrWhiteSpace(area))
                {
                    whereParts.Add("UPPER(TRIM(area)) = UPPER(@area)");
                    cmd.Parameters.Add(new DB2Parameter("@area", area.Trim()));
                }

                var where = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";
                cmd.CommandText = sql.Replace("/**where**/", where);

                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    result.Add(new
                    {
                        id = dr.GetInt32(0),
                        cargo = dr.IsDBNull(1) ? "" : dr.GetString(1).Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ListarCargosAdministrativos(canal={Canal}, area={Area})", canal, area);
            }
            return result;
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ListarCanales");
            }
            return canales;
        }

        public bool GuardarSeleccionado(BackendRequisicionPersonal.Models.SeleccionadoDto dto)
        {
            try
            {
                if (dto is null || dto.Id <= 0) return false;

                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET aprobaciones_ingreso         = @aprobaciones_ingreso,
                       nombre_seleccionado          = @nombre_seleccionado,
                       identificacion_seleccionado  = @identificacion_seleccionado,
                       fecha_ingreso_seleccionado   = @fecha_ingreso_seleccionado,
                       tipo_contrato_seleccionado   = @tipo_contrato_seleccionado,
                       estado                       = 'EN SELECCION'
                 WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@aprobaciones_ingreso", (object?)dto.AprobacionesIngreso ?? DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@nombre_seleccionado", dto.NombreSeleccionado ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@identificacion_seleccionado", dto.IdentificacionSeleccionado ?? (object)DBNull.Value));

                var fecha = dto.FechaIngresoSeleccionado?.ToString("yyyy-MM-dd");
                cmd.Parameters.Add(new DB2Parameter("@fecha_ingreso_seleccionado", (object?)fecha ?? DBNull.Value));

                cmd.Parameters.Add(new DB2Parameter("@tipo_contrato_seleccionado", dto.TipoContratoSeleccionado ?? (object)DBNull.Value));
                cmd.Parameters.Add(new DB2Parameter("@id", dto.Id));

                var n = cmd.ExecuteNonQuery();
                _logger.LogInformation("[GuardarSeleccionado] id={Id}, rows={Rows}, estado=EN SELECCION", dto.Id, n);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GuardarSeleccionado id={Id}", dto?.Id);
                return false;
            }
        }

        public bool AplicarAccion(int id, string estado, string? motivo = null, string? actorEmail = null)
        {
            try
            {
                if (id <= 0 || string.IsNullOrWhiteSpace(estado)) return false;

                var up = estado.Trim().ToUpperInvariant();
                string decision = up switch
                {
                    "APROBADA" => "APROBADA",
                    "RECHAZADA" => "RECHAZADA",
                    _ => ""
                };
                if (string.IsNullOrEmpty(decision)) return false;

                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                using var tx = cn.BeginTransaction();

                var selSql = @"
                    SELECT nivel_aprobacion, estado,
                           ap1_correo, ap1_estado,
                           ap2_correo, ap2_estado,
                           ap3_correo, ap3_estado
                    FROM solicitudes_aprobaciones_personal
                    WHERE id = @id";

                string nivelActual, estadoGeneral;
                string? ap1Correo, ap1Estado, ap2Correo, ap2Estado, ap3Correo, ap3Estado;

                using (var sel = new DB2Command(selSql, cn, tx))
                {
                    sel.Parameters.Add(new DB2Parameter("@id", id));
                    using var r = sel.ExecuteReader();
                    if (!r.Read()) return false;

                    nivelActual = r.IsDBNull(0) ? "FINAL" : r.GetString(0).Trim().ToUpperInvariant();
                    estadoGeneral = r.IsDBNull(1) ? "EN APROBACION" : r.GetString(1).Trim().ToUpperInvariant();

                    ap1Correo = r.IsDBNull(2) ? null : r.GetString(2).Trim();
                    ap1Estado = r.IsDBNull(3) ? null : r.GetString(3).Trim().ToUpperInvariant();

                    ap2Correo = r.IsDBNull(4) ? null : r.GetString(4).Trim();
                    ap2Estado = r.IsDBNull(5) ? null : r.GetString(5).Trim().ToUpperInvariant();

                    ap3Correo = r.IsDBNull(6) ? null : r.GetString(6).Trim();
                    ap3Estado = r.IsDBNull(7) ? null : r.GetString(7).Trim().ToUpperInvariant();
                }

                // === Rechazo SIEMPRE posible (incluye EN SELECCION, FINAL, EN VP GH si llegara por aquí) ===
                if (decision == "RECHAZADA")
                {
                    var rejectSql = @"
                        UPDATE solicitudes_aprobaciones_personal
                           SET estado = 'RECHAZADA',
                               nivel_aprobacion = 'FINAL',
                               fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                         WHERE id = @id";
                    using var rej = new DB2Command(rejectSql, cn, tx);
                    rej.Parameters.Add(new DB2Parameter("@id", id));
                    rej.ExecuteNonQuery();

                    // (Opcional) Guardar motivo en el nivel actual si está en 1/2/3
                    if (nivelActual == "1" || nivelActual == "2" || nivelActual == "3")
                    {
                        var pref = $"ap{nivelActual}_";
                        var motSql = $@"
                            UPDATE solicitudes_aprobaciones_personal
                               SET {pref}estado = 'RECHAZADA',
                                   {pref}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                                   {pref}motivo = @motivo
                             WHERE id = @id";
                        using var mot = new DB2Command(motSql, cn, tx);
                        mot.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                        mot.Parameters.Add(new DB2Parameter("@id", id));
                        mot.ExecuteNonQuery();
                    }

                    tx.Commit();
                    return true;
                }

                // Si llega APROBADA pero el flujo ya es FINAL o no está en EN APROBACION, no mover (salvo que quieras otro comportamiento)
                if (nivelActual == "FINAL" || estadoGeneral is "APROBADA" or "RECHAZADA" or "CERRADO" or "EN SELECCION" or "EN VP GH")
                {
                    tx.Commit();
                    return true;
                }

                int lvl = nivelActual switch { "1" => 1, "2" => 2, "3" => 3, _ => 1 };
                string? correoEsperado = lvl switch
                {
                    1 => ap1Correo,
                    2 => ap2Correo,
                    3 => ap3Correo,
                    _ => ap1Correo
                };

                if (!string.IsNullOrWhiteSpace(actorEmail) && !string.IsNullOrWhiteSpace(correoEsperado))
                {
                    if (!actorEmail.Trim().Equals(correoEsperado.Trim(), StringComparison.OrdinalIgnoreCase))
                        _logger.LogWarning("Actor {Actor} != aprobador esperado {Esp} id={Id} nivel={Nivel}", actorEmail, correoEsperado, id, lvl);
                }

                string prefix = $"ap{lvl}_";
                string setCorreo = string.IsNullOrWhiteSpace(actorEmail)
                    ? ""
                    : $", {prefix}correo = CASE WHEN {prefix}correo IS NULL OR TRIM({prefix}correo) = '' " +
                      $"THEN CAST(@actorEmail AS CHAR(100)) ELSE {prefix}correo END";

                string NextNivelCalc(string curr) =>
                    curr == "1"
                        ? (!string.IsNullOrWhiteSpace(ap2Correo) ? "2" : (!string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL"))
                        : (curr == "2"
                            ? (!string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL")
                            : "FINAL");

                string nextNivel = decision == "APROBADA" ? NextNivelCalc(lvl.ToString()) : "FINAL";
                bool finalizaCadena = (nextNivel == "FINAL");

                // Si finaliza la cadena de aprobadores, pasamos a EN SELECCION (no APROBADA)
                var updSql = $@"
                    UPDATE solicitudes_aprobaciones_personal
                       SET {prefix}estado = 'APROBADA',
                           {prefix}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           {prefix}motivo = @motivo
                           {setCorreo}
                         , estado = CASE WHEN @finaliza = 1 THEN 'EN SELECCION' ELSE 'EN APROBACION' END
                         , nivel_aprobacion = CASE WHEN @finaliza = 1 THEN 'FINAL' ELSE @nextNivel END
                         , fecha_aprobacion = CASE WHEN @finaliza = 1
                                                   THEN TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                                                   ELSE fecha_aprobacion
                                              END
                     WHERE id = @id";

                using (var upd = new DB2Command(updSql, cn, tx))
                {
                    upd.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                    if (!string.IsNullOrWhiteSpace(actorEmail))
                        upd.Parameters.Add(new DB2Parameter("@actorEmail", actorEmail.Trim()));
                    upd.Parameters.Add(new DB2Parameter("@finaliza", finalizaCadena ? 1 : 0));
                    upd.Parameters.Add(new DB2Parameter("@nextNivel", nextNivel));
                    upd.Parameters.Add(new DB2Parameter("@id", id));
                    upd.ExecuteNonQuery();
                }

                // Auto-aprobación si los siguientes niveles usan el MISMO correo del actor/aprobador actual
                if (!finalizaCadena)
                {
                    static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
                    var correoAprobadorReal = !string.IsNullOrWhiteSpace(actorEmail) ? actorEmail! : (correoEsperado ?? "");
                    var curr = nextNivel;

                    while (curr != "FINAL")
                    {
                        string? correoNivel = curr switch
                        {
                            "2" => ap2Correo,
                            "3" => ap3Correo,
                            _ => null
                        };

                        if (!string.IsNullOrWhiteSpace(correoNivel) && Norm(correoNivel) == Norm(correoAprobadorReal))
                        {
                            string siguiente = NextNivelCalc(curr);
                            bool finAuto = (siguiente == "FINAL");

                            var sqlAuto = $@"
                                UPDATE solicitudes_aprobaciones_personal
                                   SET ap{curr}_estado = 'APROBADA',
                                       ap{curr}_fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                                       estado = CASE WHEN @fin = 1 THEN 'EN SELECCION' ELSE 'EN APROBACION' END,
                                       nivel_aprobacion = CASE WHEN @fin = 1 THEN 'FINAL' ELSE @sig END,
                                       fecha_aprobacion = CASE WHEN @fin = 1 THEN TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S') ELSE fecha_aprobacion END
                                 WHERE id = @id";
                            using var upAuto = new DB2Command(sqlAuto, cn, tx);
                            upAuto.Parameters.Add(new DB2Parameter("@fin", finAuto ? 1 : 0));
                            upAuto.Parameters.Add(new DB2Parameter("@sig", siguiente));
                            upAuto.Parameters.Add(new DB2Parameter("@id", id));
                            upAuto.ExecuteNonQuery();

                            _logger.LogInformation("Auto-aprobado nivel {Nivel} para id={Id} (mismo correo {Correo})", curr, id, correoNivel);

                            if (finAuto) { curr = "FINAL"; break; }
                            curr = siguiente;
                            continue;
                        }
                        break;
                    }
                }

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar acción {Estado} para id {Id}", estado, id);
                return false;
            }
        }

        public bool MarcarRevisadoRrhh(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET fecha_envio_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           estado = 'EN APROBACION'
                     WHERE id = @id";
                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                var n = cmd.ExecuteNonQuery();
                _logger.LogInformation("MarcarRevisadoRrhh: id={Id}, rows={Rows}", id, n);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en MarcarRevisadoRrhh id={Id}", id);
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

        public bool CerrarRequisicion(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();
                var sql = @"
                    UPDATE solicitudes_aprobaciones_personal
                       SET estado = 'CERRADO',
                           nivel_aprobacion = 'FINAL',
                           fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                     WHERE id = @id";
                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));
                var n = cmd.ExecuteNonQuery();
                _logger.LogInformation("CerrarRequisicion: id={Id}, rows={Rows}", id, n);
                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en CerrarRequisicion id={Id}", id);
                return false;
            }
        }

        public (string Nivel, List<string> Correos) ObtenerCorreosAprobadorActual(int id)
        {
            var correos = new List<string>();
            string nivel = "FINAL";

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT
                        TRIM(nivel_aprobacion),
                        TRIM(ap1_correo), TRIM(ap1_estado),
                        TRIM(ap2_correo), TRIM(ap2_estado),
                        TRIM(ap3_correo), TRIM(ap3_estado)
                    FROM solicitudes_aprobaciones_personal
                    WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    nivel = r.IsDBNull(0) ? "FINAL" : r.GetString(0);

                    string ap1Correo = r.IsDBNull(1) ? null : r.GetString(1);
                    string ap1Estado = r.IsDBNull(2) ? null : r.GetString(2);
                    string ap2Correo = r.IsDBNull(3) ? null : r.GetString(3);
                    string ap2Estado = r.IsDBNull(4) ? null : r.GetString(4);
                    string ap3Correo = r.IsDBNull(5) ? null : r.GetString(5);
                    string ap3Estado = r.IsDBNull(6) ? null : r.GetString(6);

                    bool pendiente(string s) => string.IsNullOrWhiteSpace(s) || s.Equals("PENDIENTE", StringComparison.OrdinalIgnoreCase);

                    if (nivel == "1" && !string.IsNullOrWhiteSpace(ap1Correo) && pendiente(ap1Estado))
                        correos.Add(ap1Correo);
                    else if (nivel == "2" && !string.IsNullOrWhiteSpace(ap2Correo) && pendiente(ap2Estado))
                        correos.Add(ap2Correo);
                    else if (nivel == "3" && !string.IsNullOrWhiteSpace(ap3Correo) && pendiente(ap3Estado))
                        correos.Add(ap3Correo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerCorreosAprobadorActual id={Id}", id);
            }

            return (nivel, correos);
        }

        public SolicitudPersonal? ObtenerSolicitudPorId(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                SELECT
                  id,
                  id_solicitante, tipo, fecha_solicitud, vicepresidencia, nombre_vp,
                  jefe_inmediato, cargo_requerido, centro_costos, horario_trabajo, dias_laborales, salario_basico,
                  tipo_solicitud, tipo_contrato, meses_contrato, ciudad_trabajo, justificacion, correo_jefe,
                  canal, area, gerente_division, gerente_canal, terr_asignado, cobro_automatico, zona_ciudades,
                  clientes_cargo, canales_cargo, auxilio_movilizacion, salario_garantizado, meses_garantizado,
                  promedio_variable, requiere_moto, correo_gerente_canal, correo_gerente_division, area_solicitante,
                  cargo_jefe_inmediato, centro_costos_f, hora_inicio, hora_fin, activar_proceso_por, persona_reemplaza,
                  tipo_jornada,
                  estado, nivel_aprobacion, creado_en, fecha_envio_aprobacion, fecha_aprobacion,
                  salario_asignado, fecha_ingreso, aprobaciones_ingreso,
                  nombre_seleccionado, identificacion_seleccionado, fecha_ingreso_seleccionado, tipo_contrato_seleccionado
                FROM solicitudes_aprobaciones_personal
                WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

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

                    Estado = r.IsDBNull(42) ? null : r.GetString(42).Trim(),
                    NivelAprobacion = r.IsDBNull(43) ? null : r.GetString(43).Trim(),
                    CreadoEn = r.IsDBNull(44) ? null : r.GetString(44).Trim(),
                    FechaEnvioAprobacion = r.IsDBNull(45) ? null : r.GetString(45).Trim(),
                    FechaAprobacion = r.IsDBNull(46) ? null : r.GetString(46).Trim(),

                    SalarioAsignado = r.IsDBNull(47) ? null : r.GetString(47).Trim(),
                    FechaIngreso = r.IsDBNull(48) ? null : r.GetString(48).Trim(),
                    AprobacionesIngreso = r.IsDBNull(49) ? null : r.GetString(49).Trim(),

                    NombreSeleccionado = r.IsDBNull(50) ? null : r.GetString(50).Trim(),
                    IdentificacionSeleccionado = r.IsDBNull(51) ? null : r.GetString(51).Trim(),
                    FechaIngresoSeleccionado = r.IsDBNull(52) ? null : r.GetString(52).Trim(),
                    TipoContratoSeleccionado = r.IsDBNull(53) ? null : r.GetString(53).Trim(),
                };

                return o;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerSolicitudPorId id={Id}", id);
                return null;
            }
        }
    }
}
