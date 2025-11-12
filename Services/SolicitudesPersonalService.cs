using System;
using System.Collections.Generic;
using System.Text;
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
        private readonly IConfiguration _config;
        private readonly string _cs;

        public SolicitudesPersonalService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<SolicitudesPersonalService> logger)
        {
            _logger = logger;
            _config = configuration;

            _connectionString = env.IsProduction()
                ? configuration.GetConnectionString("InformixConnectionProduction")
                : configuration.GetConnectionString("InformixConnection");

            _logger.LogInformation("Cadena de conexión utilizada: {Name}",
                env.IsProduction() ? "InformixConnectionProduction" : "InformixConnection");
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

                // 1) Perfiles del usuario
                var perfiles = new List<string>();
                try
                {
                    var sqlPerfiles = @"
                SELECT UPPER(TRIM(p.nombre)) AS nombre
                FROM requisiciones_personal_usuarios_perfiles up
                JOIN requisiciones_personal_perfiles p ON p.id = up.perfil_id
                WHERE up.usuario_identificacion = @u
                  AND COALESCE(p.activo, 1) = 1";

                    using var cmdRoles = new DB2Command(sqlPerfiles, cn);
                    cmdRoles.Parameters.Add(new DB2Parameter("@u", usuarioId));
                    using var rr = cmdRoles.ExecuteReader();
                    while (rr.Read())
                    {
                        if (!rr.IsDBNull(0))
                            perfiles.Add(rr.GetString(0).Trim().ToUpperInvariant());
                    }
                }
                catch (Exception exRoles)
                {
                    _logger.LogWarning(exRoles, "No fue posible consultar perfiles para {UsuarioId}", usuarioId);
                }

                bool verTodo = perfiles.Any(r =>
                    r.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("GESTIONHUMANA", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("NOMINA", StringComparison.OrdinalIgnoreCase)
                );

                _logger.LogInformation("Listar(): usuario={UsuarioId} perfiles=[{Perfiles}] verTodo={VerTodo}",
                    usuarioId, string.Join(",", perfiles), verTodo);

                // 2) SELECT principal (agregamos ap1_*, ap2_*, ap3_*)
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
              nombre_seleccionado, identificacion_seleccionado, fecha_ingreso_seleccionado, tipo_contrato_seleccionado,

              -- === Aprobadores (crudos) ===
              ap1_nombre, ap1_correo, ap1_estado, ap1_fecha, ap1_motivo,
              ap2_nombre, ap2_correo, ap2_estado, ap2_fecha, ap2_motivo,
              ap3_nombre, ap3_correo, ap3_estado, ap3_fecha, ap3_motivo,

              -- === Campos UI ya calculados ===
              CASE
                WHEN ap1_correo IS NULL OR TRIM(ap1_correo) = '' THEN 'NA'
                WHEN UPPER(TRIM(COALESCE(ap1_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                WHEN UPPER(TRIM(COALESCE(ap1_estado,''))) = 'APROBADA'  THEN 'APROBADA'
                ELSE 'PENDIENTE'
              END AS ap1_estado_ui,

              CASE
                WHEN ap2_correo IS NULL OR TRIM(ap2_correo) = '' THEN 'NA'
                WHEN UPPER(TRIM(COALESCE(ap2_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                WHEN UPPER(TRIM(COALESCE(ap2_estado,''))) = 'APROBADA'  THEN 'APROBADA'
                ELSE 'PENDIENTE'
              END AS ap2_estado_ui,

              CASE
                WHEN ap3_correo IS NULL OR TRIM(ap3_correo) = '' THEN 'NA'
                WHEN UPPER(TRIM(COALESCE(ap3_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                WHEN UPPER(TRIM(COALESCE(ap3_estado,''))) = 'APROBADA'  THEN 'APROBADA'
                ELSE 'PENDIENTE'
              END AS ap3_estado_ui,

              CASE
                WHEN ap1_correo IS NOT NULL AND TRIM(ap1_correo) <> '' AND UPPER(TRIM(COALESCE(ap1_estado,''))) IN ('','PENDIENTE') THEN '1'
                WHEN ap2_correo IS NOT NULL AND TRIM(ap2_correo) <> '' AND UPPER(TRIM(COALESCE(ap2_estado,''))) IN ('','PENDIENTE') THEN '2'
                WHEN ap3_correo IS NOT NULL AND TRIM(ap3_correo) <> '' AND UPPER(TRIM(COALESCE(ap3_estado,''))) IN ('','PENDIENTE') THEN '3'
                ELSE 'FINAL'
              END AS nivel_actual_ui,

              CASE
                WHEN UPPER(TRIM(estado)) IN ('RECHAZADA','CERRADO') THEN TRIM(estado)
                WHEN (
                  (ap1_correo IS NOT NULL AND TRIM(ap1_correo) <> '' AND UPPER(TRIM(COALESCE(ap1_estado,''))) IN ('','PENDIENTE')) OR
                  (ap2_correo IS NOT NULL AND TRIM(ap2_correo) <> '' AND UPPER(TRIM(COALESCE(ap2_estado,''))) IN ('','PENDIENTE')) OR
                  (ap3_correo IS NOT NULL AND TRIM(ap3_correo) <> '' AND UPPER(TRIM(COALESCE(ap3_estado,''))) IN ('','PENDIENTE'))
                ) THEN 'EN APROBACION'
                WHEN UPPER(TRIM(estado)) IN ('EN VP GH','EN SELECCION','APROBADA') THEN TRIM(estado)
                WHEN UPPER(TRIM(estado)) = 'EN APROBACION' THEN 'EN SELECCION'
                ELSE TRIM(estado)
              END AS estado_ui
            FROM solicitudes_aprobaciones_personal";

                if (!verTodo)
                    sql += " WHERE id_solicitante = @u";

                sql += " ORDER BY id DESC";

                using var cmd = new DB2Command(sql, cn);
                if (!verTodo)
                    cmd.Parameters.Add(new DB2Parameter("@u", usuarioId));

                using var r = cmd.ExecuteReader();

                static string? S(DB2DataReader rd, int i) => (i < rd.FieldCount && !rd.IsDBNull(i)) ? rd.GetString(i).Trim() : null;

                while (r.Read())
                {
                    var o = new SolicitudPersonal
                    {
                        Id = r.GetInt32(0),

                        IdSolicitante = S(r, 1),
                        Tipo = S(r, 2),
                        FechaSolicitud = S(r, 3),
                        Vicepresidencia = S(r, 4),
                        NombreVp = S(r, 5),

                        JefeInmediato = S(r, 6),
                        CargoRequerido = S(r, 7),
                        CentroCostos = S(r, 8),
                        HorarioTrabajo = S(r, 9),
                        DiasLaborales = S(r, 10),
                        SalarioBasico = S(r, 11),
                        TipoSolicitud = S(r, 12),
                        TipoContrato = S(r, 13),
                        MesesContrato = S(r, 14),
                        CiudadTrabajo = S(r, 15),
                        Justificacion = S(r, 16),
                        CorreoJefe = S(r, 17),

                        Canal = S(r, 18),
                        Area = S(r, 19),
                        GerenteDivision = S(r, 20),
                        GerenteCanal = S(r, 21),
                        TerrAsignado = S(r, 22),
                        CobroAutomatico = S(r, 23),
                        ZonaCiudades = S(r, 24),
                        ClientesCargo = S(r, 25),
                        CanalesCargo = S(r, 26),
                        AuxilioMovilizacion = S(r, 27),
                        SalarioGarantizado = S(r, 28),
                        MesesGarantizado = S(r, 29),
                        PromedioVariable = S(r, 30),
                        RequiereMoto = S(r, 31),
                        CorreoGerenteCanal = S(r, 32),
                        CorreoGerenteDivision = S(r, 33),

                        AreaSolicitante = S(r, 34),
                        CargoJefeInmediato = S(r, 35),
                        CentroCostosF = S(r, 36),
                        HoraInicio = S(r, 37),
                        HoraFin = S(r, 38),
                        ActivarProcesoPor = S(r, 39),
                        PersonaReemplaza = S(r, 40),
                        TipoJornada = S(r, 41),

                        Estado = S(r, 42),
                        NivelAprobacion = S(r, 43),
                        CreadoEn = S(r, 44),
                        FechaEnvioAprobacion = S(r, 45),
                        FechaAprobacion = S(r, 46),

                        SalarioAsignado = S(r, 47),
                        FechaIngreso = S(r, 48),
                        AprobacionesIngreso = S(r, 49),

                        NombreSeleccionado = S(r, 50),
                        IdentificacionSeleccionado = S(r, 51),
                        FechaIngresoSeleccionado = S(r, 52),
                        TipoContratoSeleccionado = S(r, 53),

                        // --- Aprobadores crudos ---
                        Ap1Nombre = S(r, 54),
                        Ap1Correo = S(r, 55),
                        Ap1Estado = S(r, 56),
                        Ap1Fecha = S(r, 57),
                        Ap1Motivo = S(r, 58),

                        Ap2Nombre = S(r, 59),
                        Ap2Correo = S(r, 60),
                        Ap2Estado = S(r, 61),
                        Ap2Fecha = S(r, 62),
                        Ap2Motivo = S(r, 63),

                        Ap3Nombre = S(r, 64),
                        Ap3Correo = S(r, 65),
                        Ap3Estado = S(r, 66),
                        Ap3Fecha = S(r, 67),
                        Ap3Motivo = S(r, 68),

                        // --- Campos UI calculados (índices desplazados) ---
                        Ap1EstadoUi = S(r, 69),
                        Ap2EstadoUi = S(r, 70),
                        Ap3EstadoUi = S(r, 71),
                        NivelActualUi = S(r, 72),
                        EstadoUi = S(r, 73),
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
        public List<CargoCanal> ListarCargosCanales(string? area = null)
        {
            var list = new List<CargoCanal>();
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
        SELECT id,
               TRIM(cargo) AS cargo,
               TRIM(centro_costos) AS centro_costos,
               TRIM(area) AS area,
               TRIM(canal) AS canal
        FROM cargos_canales_requisicion_personal
        /**where**/
        ORDER BY area, cargo";

                var where = "";
                using var cmd = new DB2Command();
                cmd.Connection = cn;

                if (!string.IsNullOrWhiteSpace(area))
                {
                    where = "WHERE UPPER(TRIM(area)) = UPPER(@area)";
                    cmd.Parameters.Add(new DB2Parameter("@area", area.Trim()));
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
                        Area = r.IsDBNull(3) ? null : r.GetString(3).Trim(), 
                        Canal = r.IsDBNull(4) ? null : r.GetString(4).Trim()
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
        public List<dynamic> ListarCargosAdministrativos(string? area)
        {
            var result = new List<dynamic>();
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
            SELECT id,
                   TRIM(cargo) AS cargo,
                   TRIM(canal) AS canal,
                   TRIM(area) AS area,
                   TRIM(centro_costos) AS centro_costos
            FROM cargos_canales_requisicio_personal_administrativo
            /**where**/
            ORDER BY cargo";

                using var cmd = new DB2Command();
                cmd.Connection = cn;

                string where = "";
                if (!string.IsNullOrWhiteSpace(area))
                {
                    where = "WHERE UPPER(TRIM(area)) = UPPER(@area)";
                    cmd.Parameters.Add(new DB2Parameter("@area", area.Trim()));
                }

                cmd.CommandText = sql.Replace("/**where**/", where);

                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    var id = dr.IsDBNull(0) ? 0 : dr.GetInt32(0);
                    var cargoVal = dr.IsDBNull(1) ? "" : dr.GetString(1).Trim();
                    var canalVal = dr.IsDBNull(2) ? "" : dr.GetString(2).Trim();
                    var areaVal = dr.IsDBNull(3) ? "" : dr.GetString(3).Trim();
                    var centroCostosVal = dr.IsDBNull(4) ? "" : dr.GetString(4).Trim();

                    if (id > 0 && !string.IsNullOrWhiteSpace(cargoVal))
                    {
                        result.Add(new
                        {
                            id,
                            cargo = cargoVal,
                            canal = canalVal,
                            area = areaVal,
                            centro_costos = centroCostosVal
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ListarCargosAdministrativos(area={Area})", area);
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
                    FROM cargos_canales_requisicion_personal
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
               estado                       = 'EN VP GH'
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
                _logger.LogInformation("[GuardarSeleccionado] id={Id}, rows={Rows}, estado=EN VP GH", dto.Id, n);

                // (Opcional) var (asunto, html) = ConstruirCorreoRequisicion(dto.Id);

                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GuardarSeleccionado id={Id}", dto?.Id);
                return false;
            }
        }
        public bool AplicarAccion(int id, string estado, string? motivo = null, string? actorEmail = null, string? actorNombre = null)
        {
            try
            {
                if (id <= 0 || string.IsNullOrWhiteSpace(estado)) return false;

                var up = estado.Trim().ToUpperInvariant();
                string decision = up switch
                {
                    "APROBADA" => "APROBADA",
                    "RECHAZADA" => "RECHAZADA",
                    "CERRADO" => "CERRADO",
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

                if (nivelActual == "FINAL")
                {
                    if (!string.IsNullOrEmpty(ap1Correo) && (string.IsNullOrEmpty(ap1Estado) || ap1Estado == "PENDIENTE"))
                        nivelActual = "1";
                    else if (!string.IsNullOrEmpty(ap2Correo) && (string.IsNullOrEmpty(ap2Estado) || ap2Estado == "PENDIENTE"))
                        nivelActual = "2";
                    else if (!string.IsNullOrEmpty(ap3Correo) && (string.IsNullOrEmpty(ap3Estado) || ap3Estado == "PENDIENTE"))
                        nivelActual = "3";
                }
                
                if (decision == "RECHAZADA")
                {
                    string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // 1️⃣ Rechaza el nivel actual con motivo
                    if (nivelActual == "1" || nivelActual == "2" || nivelActual == "3")
                    {
                        var pref = $"ap{nivelActual}_";
                        var sqlNivel = $@"
                    UPDATE solicitudes_aprobaciones_personal
                       SET {pref}estado = 'RECHAZADA',
                           {pref}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                           {pref}motivo = @motivo
                     WHERE id = @id";
                        using var cmdNivel = new DB2Command(sqlNivel, cn, tx);
                        cmdNivel.Parameters.Add(new DB2Parameter("@motivo", (object?)motivo ?? DBNull.Value));
                        cmdNivel.Parameters.Add(new DB2Parameter("@id", id));
                        cmdNivel.ExecuteNonQuery();
                    }

                    // 2️⃣ Marca la requisición como rechazada globalmente
                    var sqlMain = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'RECHAZADA',
                       nivel_aprobacion = 'FINAL',
                       fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                 WHERE id = @id";
                    using var cmdMain = new DB2Command(sqlMain, cn, tx);
                    cmdMain.Parameters.Add(new DB2Parameter("@id", id));
                    cmdMain.ExecuteNonQuery();

                    tx.Commit();
                    _logger.LogInformation("[AplicarAccion] Rechazo registrado correctamente para id={Id}, motivo={Motivo}", id, motivo);
                    return true;
                }

                // === BLOQUE CIERRE ===
                if (decision == "CERRADO")
                {
                    if (!string.Equals(estadoGeneral, "EN VP GH", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Intento de CERRADO estando en {Estado} (id={Id}). Se ignora.", estadoGeneral, id);
                        tx.Commit();
                        return true;
                    }

                    var closeSql = @"
                UPDATE solicitudes_aprobaciones_personal
                   SET estado = 'CERRADO',
                       nivel_aprobacion = 'FINAL',
                       fecha_aprobacion = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S')
                 WHERE id = @id";

                    using (var closeCmd = new DB2Command(closeSql, cn, tx))
                    {
                        closeCmd.Parameters.Add(new DB2Parameter("@id", id));
                        closeCmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                    return true;
                }

                // === BLOQUE APROBACIÓN ===
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

                string setCorreoYNombre = "";
                if (!string.IsNullOrWhiteSpace(actorEmail))
                {
                    setCorreoYNombre = $@",
               {prefix}correo = CASE WHEN {prefix}correo IS NULL OR TRIM({prefix}correo) = ''
                                     THEN CAST(@actorEmail AS CHAR(100)) ELSE {prefix}correo END,
               {prefix}nombre = CASE WHEN {prefix}nombre IS NULL OR TRIM({prefix}nombre) = ''
                                     THEN CAST(@actorNombre AS CHAR(100)) ELSE {prefix}nombre END";
                }

                string NextNivelCalc(string curr) =>
                    curr == "1"
                        ? (!string.IsNullOrWhiteSpace(ap2Correo) ? "2" : (!string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL"))
                        : (curr == "2"
                            ? (!string.IsNullOrWhiteSpace(ap3Correo) ? "3" : "FINAL")
                            : "FINAL");

                string nextNivel = NextNivelCalc(lvl.ToString());
                bool finalizaCadena = (nextNivel == "FINAL");

                var updSql = $@"
            UPDATE solicitudes_aprobaciones_personal
               SET {prefix}estado = 'APROBADA',
                   {prefix}fecha  = TO_CHAR(CURRENT, '%Y-%m-%d %H:%M:%S'),
                   {prefix}motivo = @motivo
                   {setCorreoYNombre}
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
                    {
                        upd.Parameters.Add(new DB2Parameter("@actorEmail", actorEmail.Trim()));
                        upd.Parameters.Add(new DB2Parameter("@actorNombre", (object?)actorNombre ?? DBNull.Value));
                    }
                    upd.Parameters.Add(new DB2Parameter("@finaliza", finalizaCadena ? 1 : 0));
                    upd.Parameters.Add(new DB2Parameter("@nextNivel", nextNivel));
                    upd.Parameters.Add(new DB2Parameter("@id", id));
                    upd.ExecuteNonQuery();
                }

                // Auto-aprobación si siguientes niveles tienen el MISMO correo del aprobador actual
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

                // (Opcional) var (asunto, html) = ConstruirCorreoRequisicion(id);

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

                // (Opcional) var (asunto, html) = ConstruirCorreoRequisicion(id);

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

                // (Opcional) var (asunto, html) = ConstruirCorreoRequisicion(id);

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
              nombre_seleccionado, identificacion_seleccionado, fecha_ingreso_seleccionado, tipo_contrato_seleccionado,

              -- Aprobadores crudos
              ap1_nombre, ap1_correo, ap1_estado, ap1_fecha, ap1_motivo,
              ap2_nombre, ap2_correo, ap2_estado, ap2_fecha, ap2_motivo,
              ap3_nombre, ap3_correo, ap3_estado, ap3_fecha, ap3_motivo
            FROM solicitudes_aprobaciones_personal
            WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                static string? S(DB2DataReader rd, int i) => (!rd.IsDBNull(i) ? rd.GetString(i).Trim() : null);

                var o = new SolicitudPersonal
                {
                    Id = r.GetInt32(0),

                    IdSolicitante = S(r, 1),
                    Tipo = S(r, 2),
                    FechaSolicitud = S(r, 3),
                    Vicepresidencia = S(r, 4),
                    NombreVp = S(r, 5),

                    JefeInmediato = S(r, 6),
                    CargoRequerido = S(r, 7),
                    CentroCostos = S(r, 8),
                    HorarioTrabajo = S(r, 9),
                    DiasLaborales = S(r, 10),
                    SalarioBasico = S(r, 11),
                    TipoSolicitud = S(r, 12),
                    TipoContrato = S(r, 13),
                    MesesContrato = S(r, 14),
                    CiudadTrabajo = S(r, 15),
                    Justificacion = S(r, 16),
                    CorreoJefe = S(r, 17),

                    Canal = S(r, 18),
                    Area = S(r, 19),
                    GerenteDivision = S(r, 20),
                    GerenteCanal = S(r, 21),
                    TerrAsignado = S(r, 22),
                    CobroAutomatico = S(r, 23),
                    ZonaCiudades = S(r, 24),
                    ClientesCargo = S(r, 25),
                    CanalesCargo = S(r, 26),
                    AuxilioMovilizacion = S(r, 27),
                    SalarioGarantizado = S(r, 28),
                    MesesGarantizado = S(r, 29),
                    PromedioVariable = S(r, 30),
                    RequiereMoto = S(r, 31),
                    CorreoGerenteCanal = S(r, 32),
                    CorreoGerenteDivision = S(r, 33),

                    AreaSolicitante = S(r, 34),
                    CargoJefeInmediato = S(r, 35),
                    CentroCostosF = S(r, 36),
                    HoraInicio = S(r, 37),
                    HoraFin = S(r, 38),
                    ActivarProcesoPor = S(r, 39),
                    PersonaReemplaza = S(r, 40),
                    TipoJornada = S(r, 41),

                    Estado = S(r, 42),
                    NivelAprobacion = S(r, 43),
                    CreadoEn = S(r, 44),
                    FechaEnvioAprobacion = S(r, 45),
                    FechaAprobacion = S(r, 46),

                    SalarioAsignado = S(r, 47),
                    FechaIngreso = S(r, 48),
                    AprobacionesIngreso = S(r, 49),

                    NombreSeleccionado = S(r, 50),
                    IdentificacionSeleccionado = S(r, 51),
                    FechaIngresoSeleccionado = S(r, 52),
                    TipoContratoSeleccionado = S(r, 53),

                    // Aprobadores
                    Ap1Nombre = S(r, 54),
                    Ap1Correo = S(r, 55),
                    Ap1Estado = S(r, 56),
                    Ap1Fecha = S(r, 57),
                    Ap1Motivo = S(r, 58),

                    Ap2Nombre = S(r, 59),
                    Ap2Correo = S(r, 60),
                    Ap2Estado = S(r, 61),
                    Ap2Fecha = S(r, 62),
                    Ap2Motivo = S(r, 63),

                    Ap3Nombre = S(r, 64),
                    Ap3Correo = S(r, 65),
                    Ap3Estado = S(r, 66),
                    Ap3Fecha = S(r, 67),
                    Ap3Motivo = S(r, 68),
                };

                return o;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerSolicitudPorId id={Id}", id);
                return null;
            }
        }
        public (string Asunto, string Html) ConstruirCorreoRequisicion(int id)
        {
            string estado = "DESCONOCIDO";
            string nivelAprob = "FINAL";
            string accionRequeridaPor = "—";

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                const string sql = @"
                SELECT
                    id,
                    TRIM(estado),
                    TRIM(nivel_aprobacion),
                    TRIM(ap1_correo), TRIM(ap1_estado),
                    TRIM(ap2_correo), TRIM(ap2_estado),
                    TRIM(ap3_correo), TRIM(ap3_estado)
                FROM solicitudes_aprobaciones_personal
                WHERE id = @id";

                using (var cmd = new DB2Command(sql, cn))
                {
                    cmd.Parameters.Add(new DB2Parameter("@id", id));
                    using var r = cmd.ExecuteReader();
                    if (!r.Read())
                        throw new InvalidOperationException($"No existe la requisición con id {id}.");

                    estado = r.IsDBNull(1) ? "DESCONOCIDO" : r.GetString(1);
                    nivelAprob = r.IsDBNull(2) ? "FINAL" : r.GetString(2);
                }

                // Determinar “Acción requerida por” en función del estado actual
                switch ((estado ?? "").Trim().ToUpperInvariant())
                {
                    case "EN APROBACION":
                        {
                            var (nivel, correos) = ObtenerCorreosAprobadorActual(id);
                            if (nivel == "1" || nivel == "2" || nivel == "3")
                            {
                                var correosTxt = (correos != null && correos.Count > 0)
                                    ? string.Join("; ", correos)
                                    : "Aprobador pendiente";
                                accionRequeridaPor = $"Aprobador nivel {nivel} ({correosTxt})";
                            }
                            else
                            {
                                accionRequeridaPor = "Aprobador";
                            }
                            break;
                        }
                    case "EN SELECCION":
                        accionRequeridaPor = "Selección / RRHH";
                        break;
                    case "EN VP GH":
                        accionRequeridaPor = "VP GH";
                        break;
                    case "APROBADA":
                    case "CERRADO":
                    case "RECHAZADA":
                        accionRequeridaPor = "—";
                        break;
                    default:
                        accionRequeridaPor = "—";
                        break;
                }

                string asunto = $"Requisición #{id} — {estado}";
                string html = $@"
                <div style=""font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;"">
                  <h2 style=""margin:0 0 10px 0;"">Requisición #{id} — {estado}</h2>
                  <table style=""border-collapse:collapse;width:100%;max-width:720px;"">
                    <tbody>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:8px;width:220px;font-weight:600;"">ID</td>
                        <td style=""border:1px solid #ddd;padding:8px;"">#{id}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:8px;font-weight:600;"">Estado actual</td>
                        <td style=""border:1px solid #ddd;padding:8px;"">{estado}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:8px;font-weight:600;"">Nivel de aprobación</td>
                        <td style=""border:1px solid #ddd;padding:8px;"">{nivelAprob}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:8px;font-weight:600;"">Acción requerida por</td>
                        <td style=""border:1px solid #ddd;padding:8px;"">{accionRequeridaPor}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>";
                return (asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al construir correo para requisición {Id}", id);

                string asuntoFallback = $"Requisición #{id} — {estado}";
                string htmlFallback = $@"
                <div style=""font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;"">
                  <h2 style=""margin:0 0 10px 0;"">Requisición #{id} — {estado}</h2>
                  <p>No fue posible obtener todos los datos para el correo. Intenta nuevamente.</p>
                </div>";
                return (asuntoFallback, htmlFallback);
            }
        }
        public async Task<List<Dictionary<string, object>>> ListarPendientesPorCorreoAprobador(string correoAprobador)
        {
            var lista = new List<Dictionary<string, object>>();

            if (string.IsNullOrWhiteSpace(correoAprobador))
                return lista;

            try
            {
                Console.WriteLine($"[SolicitudesPersonalService] Consultando pendientes para el aprobador con correo {correoAprobador}");

                using (var cn = new DB2Connection(_connectionString))
                {
                    await cn.OpenAsync();
                    using var cmd = cn.CreateCommand();

                    cmd.CommandText = @"
SELECT
    id,
    id_solicitante,
    tipo,
    fecha_solicitud,
    vicepresidencia,
    nombre_vp,
    jefe_inmediato,
    cargo_requerido,
    centro_costos,
    horario_trabajo,
    dias_laborales,
    salario_basico,
    tipo_solicitud,
    tipo_contrato,
    meses_contrato,
    ciudad_trabajo,
    justificacion,
    correo_jefe,
    canal,
    area,
    gerente_division,
    gerente_canal,
    terr_asignado,
    cobro_automatico,
    zona_ciudades,
    clientes_cargo,
    canales_cargo,
    auxilio_movilizacion,
    salario_garantizado,
    meses_garantizado,
    promedio_variable,
    requiere_moto,
    correo_gerente_canal,
    correo_gerente_division,
    area_solicitante,
    cargo_jefe_inmediato,
    centro_costos_f,
    hora_inicio,
    hora_fin,
    activar_proceso_por,
    persona_reemplaza,
    tipo_jornada,
    estado,
    nivel_aprobacion,
    creado_en,
    fecha_envio_aprobacion,
    fecha_aprobacion,
    salario_asignado,
    fecha_ingreso,
    aprobaciones_ingreso,
    nombre_seleccionado,
    identificacion_seleccionado,
    fecha_ingreso_seleccionado,
    tipo_contrato_seleccionado,
    ap1_nombre, ap1_correo, ap1_estado, ap1_fecha, ap1_motivo,
    ap2_nombre, ap2_correo, ap2_estado, ap2_fecha, ap2_motivo,
    ap3_nombre, ap3_correo, ap3_estado, ap3_fecha, ap3_motivo,

    CASE
        WHEN ap1_correo IS NULL OR TRIM(ap1_correo) = '' THEN 'NA'
        WHEN UPPER(TRIM(COALESCE(ap1_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
        WHEN UPPER(TRIM(COALESCE(ap1_estado,''))) = 'APROBADA' THEN 'APROBADA'
        ELSE 'PENDIENTE'
    END AS ap1_estado_ui,

    CASE
        WHEN ap2_correo IS NULL OR TRIM(ap2_correo) = '' THEN 'NA'
        WHEN UPPER(TRIM(COALESCE(ap2_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
        WHEN UPPER(TRIM(COALESCE(ap2_estado,''))) = 'APROBADA' THEN 'APROBADA'
        ELSE 'PENDIENTE'
    END AS ap2_estado_ui,

    CASE
        WHEN ap3_correo IS NULL OR TRIM(ap3_correo) = '' THEN 'NA'
        WHEN UPPER(TRIM(COALESCE(ap3_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
        WHEN UPPER(TRIM(COALESCE(ap3_estado,''))) = 'APROBADA' THEN 'APROBADA'
        ELSE 'PENDIENTE'
    END AS ap3_estado_ui,

    CASE
        WHEN ap1_correo IS NOT NULL AND TRIM(ap1_correo) <> '' AND UPPER(TRIM(COALESCE(ap1_estado,''))) IN ('','PENDIENTE') THEN '1'
        WHEN ap2_correo IS NOT NULL AND TRIM(ap2_correo) <> '' AND UPPER(TRIM(COALESCE(ap2_estado,''))) IN ('','PENDIENTE') THEN '2'
        WHEN ap3_correo IS NOT NULL AND TRIM(ap3_correo) <> '' AND UPPER(TRIM(COALESCE(ap3_estado,''))) IN ('','PENDIENTE') THEN '3'
        ELSE 'FINAL'
    END AS nivel_actual_ui,

    CASE
        WHEN UPPER(TRIM(estado)) IN ('RECHAZADA','CERRADO') THEN TRIM(estado)
        WHEN (
            (ap1_correo IS NOT NULL AND TRIM(ap1_correo) <> '' AND UPPER(TRIM(COALESCE(ap1_estado,''))) IN ('','PENDIENTE')) OR
            (ap2_correo IS NOT NULL AND TRIM(ap2_correo) <> '' AND UPPER(TRIM(COALESCE(ap2_estado,''))) IN ('','PENDIENTE')) OR
            (ap3_correo IS NOT NULL AND TRIM(ap3_correo) <> '' AND UPPER(TRIM(COALESCE(ap3_estado,''))) IN ('','PENDIENTE'))
        ) THEN 'EN APROBACION'
        ELSE TRIM(estado)
    END AS estado_ui
FROM solicitudes_aprobaciones_personal
WHERE (
    (UPPER(TRIM(ap1_correo)) = UPPER(@Correo) AND (ap1_estado IS NULL OR UPPER(TRIM(ap1_estado)) = 'PENDIENTE'))
 OR (UPPER(TRIM(ap2_correo)) = UPPER(@Correo) AND (ap2_estado IS NULL OR UPPER(TRIM(ap2_estado)) = 'PENDIENTE'))
 OR (UPPER(TRIM(ap3_correo)) = UPPER(@Correo) AND (ap3_estado IS NULL OR UPPER(TRIM(ap3_estado)) = 'PENDIENTE'))
)
ORDER BY fecha_solicitud DESC";

                    cmd.Parameters.Add(new DB2Parameter("@Correo", DB2Type.VarChar) { Value = correoAprobador });

                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < r.FieldCount; i++)
                        {
                            var name = r.GetName(i);
                            var value = r.IsDBNull(i) ? null : r.GetValue(i)?.ToString()?.Trim();
                            row[name] = value ?? "";
                        }
                        lista.Add(row);
                    }

                    Console.WriteLine($"[SolicitudesPersonalService] {lista.Count} pendientes encontradas para {correoAprobador}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SolicitudesPersonalService] Error consultando pendientes para {correoAprobador}: {ex.Message}");
            }

            return lista;
        }
        public async Task<List<dynamic>> GetSolicitudesVpGhAsync(string identificacion)
        {
            var resultados = new List<dynamic>();

            try
            {
                using (var cn = new DB2Connection(_connectionString))
                {
                    await cn.OpenAsync();

                    const string sql = @"
                    SELECT
                        s.id,
                        s.tipo,
                        s.cargo_requerido,
                        s.jefe_inmediato,
                        s.ciudad_trabajo,
                        s.fecha_solicitud,
                        s.estado,
                        s.id_solicitante
                    FROM solicitudes_aprobaciones_personal s
                    WHERE TRIM(UPPER(s.estado)) = 'EN VP GH'
                      AND EXISTS (
                            SELECT 1
                            FROM requisiciones_aprobaciones_personal r
                            WHERE TRIM(r.identificacion) = ?
                              AND (
                                    TRIM(UPPER(r.cargo)) = 'VICEPRESIDENTE DE GESTION HUMANA'
                                 OR TRIM(UPPER(r.area)) = 'VP GESTION HUMANA'
                              )
                      )
                    ORDER BY s.fecha_solicitud DESC";

                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.Parameters.Add(new IBM.Data.Db2.DB2Parameter { Value = identificacion });

                        using var r = await cmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                        {
                            resultados.Add(new
                            {
                                Id = r["id"]?.ToString()?.Trim(),
                                Tipo = r["tipo"]?.ToString()?.Trim(),
                                CargoRequerido = r["cargo_requerido"]?.ToString()?.Trim(),
                                JefeInmediato = r["jefe_inmediato"]?.ToString()?.Trim(),
                                CiudadTrabajo = r["ciudad_trabajo"]?.ToString()?.Trim(),
                                FechaSolicitud = r["fecha_solicitud"]?.ToString()?.Trim(),
                                Estado = r["estado"]?.ToString()?.Trim(),
                                IdSolicitante = r["id_solicitante"]?.ToString()?.Trim()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetSolicitudesVpGhAsync] Error: {ex.Message}");
            }

            return resultados;
        }

    }
}
