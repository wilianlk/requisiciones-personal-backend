using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        
        private readonly AprobacionService _aprobacionService;
        private readonly ConsultasPersonalService _consultasService;
        private readonly ReportesService _reportesService;

        public SolicitudesPersonalService(
            IConfiguration configuration,
            IWebHostEnvironment env,
            ILogger<SolicitudesPersonalService> logger,
            AprobacionService aprobacionService,
            ConsultasPersonalService consultasService,
            ReportesService reportesService)
        {
            _logger = logger;
            _config = configuration;

            _connectionString = env.IsProduction()
                ? configuration.GetConnectionString("InformixConnectionProduction")
                : configuration.GetConnectionString("InformixConnection");

            _logger.LogInformation("Cadena de conexión utilizada: {Name}",
                env.IsProduction() ? "InformixConnectionProduction" : "InformixConnection");

            _aprobacionService = aprobacionService;
            _consultasService = consultasService;
            _reportesService = reportesService;
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

                // Determinar aprobadores según tipo
                string? ap1Nombre, ap1Correo, ap2Nombre, ap2Correo, ap3Nombre, ap3Correo;

                if (tipoNorm == "ADMINISTRATIVO")
                {
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
                    // Comercial
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
                  estado, nivel_aprobacion, creado_en,
                  aprobaciones_ingreso,
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
                  @estado, @nivel_aprobacion, @creado_en,
                  NULL,
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

        public bool GuardarSeleccionado(SeleccionadoDto dto)
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
                           estado                       = 'EN NOMINA'
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
                _logger.LogInformation("[GuardarSeleccionado] id={Id}, rows={Rows}, estado=EN NOMINA", dto.Id, n);

                return n > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GuardarSeleccionado id={Id}", dto?.Id);
                return false;
            }
        }

        public List<SolicitudPersonal> Listar(string usuarioId)
        {
            var list = new List<SolicitudPersonal>();

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

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
                catch (Exception) { }

                bool esVpGh = perfiles.Any(r => r.Equals("VPGH", StringComparison.OrdinalIgnoreCase));
                bool esNomina = perfiles.Any(r => r.Equals("NOMINA", StringComparison.OrdinalIgnoreCase));
                bool verTodo = perfiles.Any(r =>
                    r.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("GESTIONHUMANA", StringComparison.OrdinalIgnoreCase)
                );

                var sql = @"
                    SELECT
                      s.id,
                      s.id_solicitante,
                      r.jefe_inmediato_solicitante AS solicitante_nombre,
                      r.e_mail AS solicitante_correo,
                      s.tipo,
                      s.fecha_solicitud,
                      s.vicepresidencia,
                      s.nombre_vp,
                      s.jefe_inmediato,
                      s.cargo_requerido,
                      s.centro_costos,
                      s.horario_trabajo,
                      s.dias_laborales,
                      s.salario_basico,
                      s.tipo_solicitud,
                      s.tipo_contrato,
                      s.meses_contrato,
                      s.ciudad_trabajo,
                      s.justificacion,
                      s.correo_jefe,
                      s.canal,
                      s.area,
                      s.gerente_division,
                      s.gerente_canal,
                      s.terr_asignado,
                      s.cobro_automatico,
                      s.zona_ciudades,
                      s.clientes_cargo,
                      s.canales_cargo,
                      s.auxilio_movilizacion,
                      s.salario_garantizado,
                      s.meses_garantizado,
                      s.promedio_variable,
                      s.requiere_moto,
                      s.correo_gerente_canal,
                      s.correo_gerente_division,
                      s.area_solicitante,
                      s.cargo_jefe_inmediato,
                      s.centro_costos_f,
                      s.hora_inicio,
                      s.hora_fin,
                      s.activar_proceso_por,
                      s.persona_reemplaza,
                      s.tipo_jornada,
                      s.estado,
                      s.nivel_aprobacion,
                      s.creado_en,
                      s.fecha_envio_aprobacion,
                      s.gh_rev_nombre,
                      s.gh_rev_correo,
                      s.gh_rev_fecha,
                      s.gh_rev_motivo,
                      s.fecha_aprobacion,
                      s.fecha_fin_aprobadores,
                      s.salario_asignado,
                      s.fecha_ingreso,
                      s.aprobaciones_ingreso,
                      s.nombre_seleccionado,
                      s.identificacion_seleccionado,
                      s.fecha_ingreso_seleccionado,
                      s.tipo_contrato_seleccionado,
                      s.ap1_nombre,
                      s.ap1_correo,
                      s.ap1_estado,
                      s.ap1_fecha,
                      s.ap1_motivo,
                      s.ap2_nombre,
                      s.ap2_correo,
                      s.ap2_estado,
                      s.ap2_fecha,
                      s.ap2_motivo,
                      s.ap3_nombre,
                      s.ap3_correo,
                      s.ap3_estado,
                      s.ap3_fecha,
                      s.ap3_motivo,
                      s.vp_cierre_nombre,
                      s.vp_cierre_correo,
                      s.vp_cierre_motivo,
                      s.fecha_cierre,
                      CASE
                        WHEN s.ap1_correo IS NULL OR TRIM(s.ap1_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap1_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap1_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap1_estado_ui,
                      CASE
                        WHEN s.ap2_correo IS NULL OR TRIM(s.ap2_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap2_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap2_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap2_estado_ui,
                      CASE
                        WHEN s.ap3_correo IS NULL OR TRIM(s.ap3_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap3_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap3_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap3_estado_ui,
                      CASE
                        WHEN UPPER(TRIM(s.estado)) IN ('RECHAZADA','CERRADO','RECHAZADO POR GESTION GH','RECHAZADO POR VP GH') THEN 'FINAL'
                        WHEN UPPER(TRIM(s.estado)) = 'EN REVISION POR GESTION GH' THEN 'EN REVISION POR GESTION GH'
                        WHEN UPPER(TRIM(s.estado)) = 'EN VP GH' THEN 'EN VP GH'
                        WHEN (
                          (s.ap1_correo IS NOT NULL AND TRIM(s.ap1_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap1_estado,''))) IN ('','PENDIENTE')) OR
                          (s.ap2_correo IS NOT NULL AND TRIM(s.ap2_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap2_estado,''))) IN ('','PENDIENTE')) OR
                          (s.ap3_correo IS NOT NULL AND TRIM(s.ap3_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap3_estado,''))) IN ('','PENDIENTE'))
                        ) THEN 'EN APROBACION'
                        ELSE TRIM(s.estado)
                      END AS estado_ui
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    ";

                if (esVpGh)
                {
                    sql += " WHERE UPPER(TRIM(s.estado)) IN ('EN VP GH','CERRADO','RECHAZADO POR VP GH','RECHAZADO POR GESTION GH')";
                }
                else if (esNomina)
                {
                    sql += " WHERE UPPER(TRIM(s.estado)) = 'EN NOMINA'";
                }
                else if (!verTodo)
                {
                    sql += " WHERE s.id_solicitante = @u";
                }

                sql += " ORDER BY s.id DESC";

                using var cmd = new DB2Command(sql, cn);
                if (!esVpGh && !esNomina && !verTodo)
                    cmd.Parameters.Add(new DB2Parameter("@u", usuarioId));

                using var r = cmd.ExecuteReader();

                static string? S(DB2DataReader rd, int i) =>
                    (i < rd.FieldCount && !rd.IsDBNull(i)) ? rd.GetString(i).Trim() : null;

                while (r.Read())
                {
                    var o = new SolicitudPersonal
                    {
                        Id = r.GetInt32(0),
                        IdSolicitante = S(r, 1),
                        SolicitanteNombre = S(r, 2),
                        SolicitanteCorreo = S(r, 3),
                        Tipo = S(r, 4),
                        FechaSolicitud = S(r, 5),
                        Vicepresidencia = S(r, 6),
                        NombreVp = S(r, 7),
                        JefeInmediato = S(r, 8),
                        CargoRequerido = S(r, 9),
                        CentroCostos = S(r, 10),
                        HorarioTrabajo = S(r, 11),
                        DiasLaborales = S(r, 12),
                        SalarioBasico = S(r, 13),
                        TipoSolicitud = S(r, 14),
                        TipoContrato = S(r, 15),
                        MesesContrato = S(r, 16),
                        CiudadTrabajo = S(r, 17),
                        Justificacion = S(r, 18),
                        CorreoJefe = S(r, 19),
                        Canal = S(r, 20),
                        Area = S(r, 21),
                        GerenteDivision = S(r, 22),
                        GerenteCanal = S(r, 23),
                        TerrAsignado = S(r, 24),
                        CobroAutomatico = S(r, 25),
                        ZonaCiudades = S(r, 26),
                        ClientesCargo = S(r, 27),
                        CanalesCargo = S(r, 28),
                        AuxilioMovilizacion = S(r, 29),
                        SalarioGarantizado = S(r, 30),
                        MesesGarantizado = S(r, 31),
                        PromedioVariable = S(r, 32),
                        RequiereMoto = S(r, 33),
                        CorreoGerenteCanal = S(r, 34),
                        CorreoGerenteDivision = S(r, 35),
                        AreaSolicitante = S(r, 36),
                        CargoJefeInmediato = S(r, 37),
                        CentroCostosF = S(r, 38),
                        HoraInicio = S(r, 39),
                        HoraFin = S(r, 40),
                        ActivarProcesoPor = S(r, 41),
                        PersonaReemplaza = S(r, 42),
                        TipoJornada = S(r, 43),
                        Estado = S(r, 44),
                        NivelAprobacion = S(r, 45),
                        CreadoEn = S(r, 46),
                        FechaEnvioAprobacion = S(r, 47),
                        GhRevNombre = S(r, 48),
                        GhRevCorreo = S(r, 49),
                        GhRevFecha = S(r, 50),
                        GhRevMotivo = S(r, 51),
                        FechaAprobacion = S(r, 52),
                        FechaFinAprobadores = S(r, 53),
                        SalarioAsignado = S(r, 54),
                        FechaIngreso = S(r, 55),
                        AprobacionesIngreso = S(r, 56),
                        NombreSeleccionado = S(r, 57),
                        IdentificacionSeleccionado = S(r, 58),
                        FechaIngresoSeleccionado = S(r, 59),
                        TipoContratoSeleccionado = S(r, 60),
                        Ap1Nombre = S(r, 61),
                        Ap1Correo = S(r, 62),
                        Ap1Estado = S(r, 63),
                        Ap1Fecha = S(r, 64),
                        Ap1Motivo = S(r, 65),
                        Ap2Nombre = S(r, 66),
                        Ap2Correo = S(r, 67),
                        Ap2Estado = S(r, 68),
                        Ap2Fecha = S(r, 69),
                        Ap2Motivo = S(r, 70),
                        Ap3Nombre = S(r, 71),
                        Ap3Correo = S(r, 72),
                        Ap3Estado = S(r, 73),
                        Ap3Fecha = S(r, 74),
                        Ap3Motivo = S(r, 75),
                        VpCierreNombre = S(r, 76),
                        VpCierreCorreo = S(r, 77),
                        VpCierreMotivo = S(r, 78),
                        FechaCierre = S(r, 79),
                        Ap1EstadoUi = S(r, 80),
                        Ap2EstadoUi = S(r, 81),
                        Ap3EstadoUi = S(r, 82),
                        NivelActualUi = S(r, 83),
                        EstadoUi = S(r, 84)
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

        public SolicitudPersonal? ObtenerSolicitudPorId(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT
                      s.id,
                      s.id_solicitante,
                      r.jefe_inmediato_solicitante AS solicitante_nombre,
                      r.e_mail AS solicitante_correo,
                      s.tipo, s.fecha_solicitud, s.vicepresidencia, s.nombre_vp,
                      s.jefe_inmediato, s.cargo_requerido, s.centro_costos, s.horario_trabajo, s.dias_laborales, s.salario_basico,
                      s.tipo_solicitud, s.tipo_contrato, s.meses_contrato, s.ciudad_trabajo, s.justificacion, s.correo_jefe,
                      s.canal, s.area, s.gerente_division, s.gerente_canal, s.terr_asignado, s.cobro_automatico, s.zona_ciudades,
                      s.clientes_cargo, s.canales_cargo, s.auxilio_movilizacion, s.salario_garantizado, s.meses_garantizado, s.promedio_variable,
                      s.requiere_moto, s.correo_gerente_canal, s.correo_gerente_division, s.area_solicitante, s.cargo_jefe_inmediato,
                      s.centro_costos_f, s.hora_inicio, s.hora_fin, s.activar_proceso_por, s.persona_reemplaza, s.tipo_jornada,
                      s.estado, s.nivel_aprobacion, s.creado_en,
                      s.fecha_envio_aprobacion, s.gh_rev_nombre, s.gh_rev_correo, s.gh_rev_fecha, s.gh_rev_motivo,
                      s.fecha_aprobacion, s.fecha_fin_aprobadores,
                      s.salario_asignado, s.fecha_ingreso, s.aprobaciones_ingreso,
                      s.nombre_seleccionado, s.identificacion_seleccionado, s.fecha_ingreso_seleccionado, s.tipo_contrato_seleccionado,
                      s.ap1_nombre, s.ap1_correo, s.ap1_estado, s.ap1_fecha, s.ap1_motivo,
                      s.ap2_nombre, s.ap2_correo, s.ap2_estado, s.ap2_fecha, s.ap2_motivo,
                      s.ap3_nombre, s.ap3_correo, s.ap3_estado, s.ap3_fecha, s.ap3_motivo,
                      s.vp_cierre_nombre, s.vp_cierre_correo, s.vp_cierre_motivo, s.fecha_cierre
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    WHERE s.id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                static string? S(DB2DataReader rd, int i) =>
                    (i < rd.FieldCount && !rd.IsDBNull(i)) ? rd.GetString(i).Trim() : null;

                return new SolicitudPersonal
                {
                    Id = r.GetInt32(0),
                    IdSolicitante = S(r, 1),
                    SolicitanteNombre = S(r, 2),
                    SolicitanteCorreo = S(r, 3),
                    Tipo = S(r, 4),
                    FechaSolicitud = S(r, 5),
                    Vicepresidencia = S(r, 6),
                    NombreVp = S(r, 7),
                    JefeInmediato = S(r, 8),
                    CargoRequerido = S(r, 9),
                    CentroCostos = S(r, 10),
                    HorarioTrabajo = S(r, 11),
                    DiasLaborales = S(r, 12),
                    SalarioBasico = S(r, 13),
                    TipoSolicitud = S(r, 14),
                    TipoContrato = S(r, 15),
                    MesesContrato = S(r, 16),
                    CiudadTrabajo = S(r, 17),
                    Justificacion = S(r, 18),
                    CorreoJefe = S(r, 19),
                    Canal = S(r, 20),
                    Area = S(r, 21),
                    GerenteDivision = S(r, 22),
                    GerenteCanal = S(r, 23),
                    TerrAsignado = S(r, 24),
                    CobroAutomatico = S(r, 25),
                    ZonaCiudades = S(r, 26),
                    ClientesCargo = S(r, 27),
                    CanalesCargo = S(r, 28),
                    AuxilioMovilizacion = S(r, 29),
                    SalarioGarantizado = S(r, 30),
                    MesesGarantizado = S(r, 31),
                    PromedioVariable = S(r, 32),
                    RequiereMoto = S(r, 33),
                    CorreoGerenteCanal = S(r, 34),
                    CorreoGerenteDivision = S(r, 35),
                    AreaSolicitante = S(r, 36),
                    CargoJefeInmediato = S(r, 37),
                    CentroCostosF = S(r, 38),
                    HoraInicio = S(r, 39),
                    HoraFin = S(r, 40),
                    ActivarProcesoPor = S(r, 41),
                    PersonaReemplaza = S(r, 42),
                    TipoJornada = S(r, 43),
                    Estado = S(r, 44),
                    NivelAprobacion = S(r, 45),
                    CreadoEn = S(r, 46),
                    FechaEnvioAprobacion = S(r, 47),
                    GhRevNombre = S(r, 48),
                    GhRevCorreo = S(r, 49),
                    GhRevFecha = S(r, 50),
                    GhRevMotivo = S(r, 51),
                    FechaAprobacion = S(r, 52),
                    FechaFinAprobadores = S(r, 53),
                    SalarioAsignado = S(r, 54),
                    FechaIngreso = S(r, 55),
                    AprobacionesIngreso = S(r, 56),
                    NombreSeleccionado = S(r, 57),
                    IdentificacionSeleccionado = S(r, 58),
                    FechaIngresoSeleccionado = S(r, 59),
                    TipoContratoSeleccionado = S(r, 60),
                    Ap1Nombre = S(r, 61),
                    Ap1Correo = S(r, 62),
                    Ap1Estado = S(r, 63),
                    Ap1Fecha = S(r, 64),
                    Ap1Motivo = S(r, 65),
                    Ap2Nombre = S(r, 66),
                    Ap2Correo = S(r, 67),
                    Ap2Estado = S(r, 68),
                    Ap2Fecha = S(r, 69),
                    Ap2Motivo = S(r, 70),
                    Ap3Nombre = S(r, 71),
                    Ap3Correo = S(r, 72),
                    Ap3Estado = S(r, 73),
                    Ap3Fecha = S(r, 74),
                    Ap3Motivo = S(r, 75),
                    VpCierreNombre = S(r, 76),
                    VpCierreCorreo = S(r, 77),
                    VpCierreMotivo = S(r, 78),
                    FechaCierre = S(r, 79)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerSolicitudPorId id={Id}", id);
                return null;
            }
        }

        public (string Nivel, List<string> Correos) ObtenerCorreosAprobadorCurrent(int id)
        {
            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT nivel_aprobacion, ap1_correo, ap2_correo, ap3_correo
                    FROM solicitudes_aprobaciones_personal
                    WHERE id = @id";

                using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@id", id));

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return ("FINAL", new List<string>());

                var nivel = r.IsDBNull(0) ? "FINAL" : r.GetString(0).Trim();
                var ap1 = r.IsDBNull(1) ? null : r.GetString(1).Trim();
                var ap2 = r.IsDBNull(2) ? null : r.GetString(2).Trim();
                var ap3 = r.IsDBNull(3) ? null : r.GetString(3).Trim();

                var correos = new List<string>();
                switch (nivel)
                {
                    case "1":
                        if (!string.IsNullOrWhiteSpace(ap1)) correos.Add(ap1);
                        break;
                    case "2":
                        if (!string.IsNullOrWhiteSpace(ap2)) correos.Add(ap2);
                        break;
                    case "3":
                        if (!string.IsNullOrWhiteSpace(ap3)) correos.Add(ap3);
                        break;
                }

                return (nivel, correos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerCorreosAprobadorCurrent id={Id}", id);
                return ("FINAL", new List<string>());
            }
        }

        public async Task<List<Dictionary<string, object>>> ListarPendientesPorCorreoAprobador(string correoAprobador, string? estado = null)
        {
            var result = new List<Dictionary<string, object>>();
            try
            {
                await using var cn = new DB2Connection(_connectionString);
                await cn.OpenAsync();

                var estadoNorm = BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(estado);
                var filtrarEstado = estadoNorm is "EN NOMINA" or "EN VP GH";

                var sql = $@"
                    SELECT 
                        s.id,
                        s.tipo,
                        s.fecha_solicitud,
                        s.cargo_requerido,
                        s.estado,
                        s.nivel_aprobacion,
                        s.ap1_correo,
                        s.ap1_estado,
                        s.ap2_correo,
                        s.ap2_estado,
                        s.ap3_correo,
                        s.ap3_estado,
                        s.id_solicitante,
                        r.jefe_inmediato_solicitante
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    WHERE 1=1
                      AND (
                            (s.nivel_aprobacion = '1' AND UPPER(TRIM(s.ap1_correo)) = UPPER(TRIM(@correo)) AND UPPER(TRIM(COALESCE(s.ap1_estado,''))) IN ('','PENDIENTE'))
                         OR (s.nivel_aprobacion = '2' AND UPPER(TRIM(s.ap2_correo)) = UPPER(TRIM(@correo)) AND UPPER(TRIM(COALESCE(s.ap2_estado,''))) IN ('','PENDIENTE'))
                         OR (s.nivel_aprobacion = '3' AND UPPER(TRIM(s.ap3_correo)) = UPPER(TRIM(@correo)) AND UPPER(TRIM(COALESCE(s.ap3_estado,''))) IN ('','PENDIENTE'))
                      )
                      AND (
                           @filtrar_estado = 0
                           OR UPPER(TRIM(s.estado)) = @estado
                      )
                    ORDER BY s.id DESC";

                await using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@correo", correoAprobador.Trim()));
                cmd.Parameters.Add(new DB2Parameter("@filtrar_estado", filtrarEstado ? 1 : 0));
                cmd.Parameters.Add(new DB2Parameter("@estado", (object?)(filtrarEstado ? estadoNorm : DBNull.Value) ?? DBNull.Value));

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var dict = new Dictionary<string, object>
                    {
                        ["id"] = r.GetInt32(0),
                        ["tipo"] = r.IsDBNull(1) ? "" : r.GetString(1).Trim(),
                        ["fecha_solicitud"] = r.IsDBNull(2) ? "" : r.GetString(2).Trim(),
                        ["cargo_requerido"] = r.IsDBNull(3) ? "" : r.GetString(3).Trim(),
                        ["estado"] = r.IsDBNull(4) ? "" : r.GetString(4).Trim(),
                        ["nivel_aprobacion"] = r.IsDBNull(5) ? "" : r.GetString(5).Trim(),
                        ["solicitante_id"] = r.IsDBNull(12) ? "" : r.GetString(12).Trim(),
                        ["solicitante_nombre"] = r.IsDBNull(13) ? "" : r.GetString(13).Trim()
                    };
                    result.Add(dict);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ListarPendientesPorCorreoAprobador correo={Correo}", correoAprobador);
            }
            return result;
        }

        public List<SolicitudPersonal> ListarDesdeConsultasPorCorreo(string correo, string? estadoNorm = null)
        {
            var list = new List<SolicitudPersonal>();

            try
            {
                using var cn = new DB2Connection(_connectionString);
                cn.Open();

                var sql = @"
                    SELECT
                      s.id,
                      s.id_solicitante,
                      r.jefe_inmediato_solicitante AS solicitante_nombre,
                      r.e_mail AS solicitante_correo,
                      s.tipo,
                      s.fecha_solicitud,
                      s.vicepresidencia,
                      s.nombre_vp,
                      s.jefe_inmediato,
                      s.cargo_requerido,
                      s.centro_costos,
                      s.horario_trabajo,
                      s.dias_laborales,
                      s.salario_basico,
                      s.tipo_solicitud,
                      s.tipo_contrato,
                      s.meses_contrato,
                      s.ciudad_trabajo,
                      s.justificacion,
                      s.correo_jefe,
                      s.canal,
                      s.area,
                      s.gerente_division,
                      s.gerente_canal,
                      s.terr_asignado,
                      s.cobro_automatico,
                      s.zona_ciudades,
                      s.clientes_cargo,
                      s.canales_cargo,
                      s.auxilio_movilizacion,
                      s.salario_garantizado,
                      s.meses_garantizado,
                      s.promedio_variable,
                      s.requiere_moto,
                      s.correo_gerente_canal,
                      s.correo_gerente_division,
                      s.area_solicitante,
                      s.cargo_jefe_inmediato,
                      s.centro_costos_f,
                      s.hora_inicio,
                      s.hora_fin,
                      s.activar_proceso_por,
                      s.persona_reemplaza,
                      s.tipo_jornada,
                      s.estado,
                      s.nivel_aprobacion,
                      s.creado_en,
                      s.fecha_envio_aprobacion,
                      s.gh_rev_nombre,
                      s.gh_rev_correo,
                      s.gh_rev_fecha,
                      s.gh_rev_motivo,
                      s.fecha_aprobacion,
                      s.fecha_fin_aprobadores,
                      s.salario_asignado,
                      s.fecha_ingreso,
                      s.aprobaciones_ingreso,
                      s.nombre_seleccionado,
                      s.identificacion_seleccionado,
                      s.fecha_ingreso_seleccionado,
                      s.tipo_contrato_seleccionado,
                      s.ap1_nombre,
                      s.ap1_correo,
                      s.ap1_estado,
                      s.ap1_fecha,
                      s.ap1_motivo,
                      s.ap2_nombre,
                      s.ap2_correo,
                      s.ap2_estado,
                      s.ap2_fecha,
                      s.ap2_motivo,
                      s.ap3_nombre,
                      s.ap3_correo,
                      s.ap3_estado,
                      s.ap3_fecha,
                      s.ap3_motivo,
                      s.vp_cierre_nombre,
                      s.vp_cierre_correo,
                      s.vp_cierre_motivo,
                      s.fecha_cierre,
                      CASE
                        WHEN s.ap1_correo IS NULL OR TRIM(s.ap1_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap1_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap1_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap1_estado_ui,
                      CASE
                        WHEN s.ap2_correo IS NULL OR TRIM(s.ap2_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap2_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap2_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap2_estado_ui,
                      CASE
                        WHEN s.ap3_correo IS NULL OR TRIM(s.ap3_correo) = '' THEN 'NA'
                        WHEN UPPER(TRIM(COALESCE(s.ap3_estado,''))) = 'RECHAZADA' THEN 'RECHAZADA'
                        WHEN UPPER(TRIM(COALESCE(s.ap3_estado,''))) = 'APROBADA' THEN 'APROBADA'
                        ELSE 'PENDIENTE'
                      END AS ap3_estado_ui,
                      CASE
                        WHEN UPPER(TRIM(s.estado)) IN ('RECHAZADA','CERRADO','RECHAZADO POR GESTION GH','RECHAZADO POR VP GH') THEN 'FINAL'
                        WHEN UPPER(TRIM(s.estado)) = 'EN REVISION POR GESTION GH' THEN 'EN REVISION POR GESTION GH'
                        WHEN UPPER(TRIM(s.estado)) = 'EN VP GH' THEN 'EN VP GH'
                        WHEN (
                          (s.ap1_correo IS NOT NULL AND TRIM(s.ap1_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap1_estado,''))) IN ('','PENDIENTE')) OR
                          (s.ap2_correo IS NOT NULL AND TRIM(s.ap2_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap2_estado,''))) IN ('','PENDIENTE')) OR
                          (s.ap3_correo IS NOT NULL AND TRIM(s.ap3_correo) <> '' AND UPPER(TRIM(COALESCE(s.ap3_estado,''))) IN ('','PENDIENTE'))
                        ) THEN 'EN APROBACION'
                        ELSE TRIM(s.estado)
                      END AS estado_ui
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    WHERE UPPER(TRIM(s.estado)) = 'EN VP GH'
                    ORDER BY s.id DESC";

                using var cmd = new DB2Command(sql, cn);
                using var r = cmd.ExecuteReader();

                static string? S(DB2DataReader rd, int i) =>
                    (i < rd.FieldCount && !rd.IsDBNull(i)) ? rd.GetString(i).Trim() : null;

                while (r.Read())
                {
                    var o = new SolicitudPersonal
                    {
                        Id = r.GetInt32(0),
                        IdSolicitante = S(r, 1),
                        SolicitanteNombre = S(r, 2),
                        SolicitanteCorreo = S(r, 3),
                        Tipo = S(r, 4),
                        FechaSolicitud = S(r, 5),
                        Vicepresidencia = S(r, 6),
                        NombreVp = S(r, 7),
                        JefeInmediato = S(r, 8),
                        CargoRequerido = S(r, 9),
                        CentroCostos = S(r, 10),
                        HorarioTrabajo = S(r, 11),
                        DiasLaborales = S(r, 12),
                        SalarioBasico = S(r, 13),
                        TipoSolicitud = S(r, 14),
                        TipoContrato = S(r, 15),
                        MesesContrato = S(r, 16),
                        CiudadTrabajo = S(r, 17),
                        Justificacion = S(r, 18),
                        CorreoJefe = S(r, 19),
                        Canal = S(r, 20),
                        Area = S(r, 21),
                        GerenteDivision = S(r, 22),
                        GerenteCanal = S(r, 23),
                        TerrAsignado = S(r, 24),
                        CobroAutomatico = S(r, 25),
                        ZonaCiudades = S(r, 26),
                        ClientesCargo = S(r, 27),
                        CanalesCargo = S(r, 28),
                        AuxilioMovilizacion = S(r, 29),
                        SalarioGarantizado = S(r, 30),
                        MesesGarantizado = S(r, 31),
                        PromedioVariable = S(r, 32),
                        RequiereMoto = S(r, 33),
                        CorreoGerenteCanal = S(r, 34),
                        CorreoGerenteDivision = S(r, 35),
                        AreaSolicitante = S(r, 36),
                        CargoJefeInmediato = S(r, 37),
                        CentroCostosF = S(r, 38),
                        HoraInicio = S(r, 39),
                        HoraFin = S(r, 40),
                        ActivarProcesoPor = S(r, 41),
                        PersonaReemplaza = S(r, 42),
                        TipoJornada = S(r, 43),
                        Estado = S(r, 44),
                        NivelAprobacion = S(r, 45),
                        CreadoEn = S(r, 46),
                        FechaEnvioAprobacion = S(r, 47),
                        GhRevNombre = S(r, 48),
                        GhRevCorreo = S(r, 49),
                        GhRevFecha = S(r, 50),
                        GhRevMotivo = S(r, 51),
                        FechaAprobacion = S(r, 52),
                        FechaFinAprobadores = S(r, 53),
                        SalarioAsignado = S(r, 54),
                        FechaIngreso = S(r, 55),
                        AprobacionesIngreso = S(r, 56),
                        NombreSeleccionado = S(r, 57),
                        IdentificacionSeleccionado = S(r, 58),
                        FechaIngresoSeleccionado = S(r, 59),
                        TipoContratoSeleccionado = S(r, 60),
                        Ap1Nombre = S(r, 61),
                        Ap1Correo = S(r, 62),
                        Ap1Estado = S(r, 63),
                        Ap1Fecha = S(r, 64),
                        Ap1Motivo = S(r, 65),
                        Ap2Nombre = S(r, 66),
                        Ap2Correo = S(r, 67),
                        Ap2Estado = S(r, 68),
                        Ap2Fecha = S(r, 69),
                        Ap2Motivo = S(r, 70),
                        Ap3Nombre = S(r, 71),
                        Ap3Correo = S(r, 72),
                        Ap3Estado = S(r, 73),
                        Ap3Fecha = S(r, 74),
                        Ap3Motivo = S(r, 75),
                        VpCierreNombre = S(r, 76),
                        VpCierreCorreo = S(r, 77),
                        VpCierreMotivo = S(r, 78),
                        FechaCierre = S(r, 79),
                        Ap1EstadoUi = S(r, 80),
                        Ap2EstadoUi = S(r, 81),
                        Ap3EstadoUi = S(r, 82),
                        NivelActualUi = S(r, 83),
                        EstadoUi = S(r, 84)
                    };

                    list.Add(o);
                }

                if (!string.IsNullOrWhiteSpace(correo))
                {
                    var correoNorm = correo.Trim().ToUpperInvariant();
                    list = list.Where(x => (x?.VpCierreCorreo ?? string.Empty).Trim().ToUpperInvariant() == correoNorm
                                         || (x?.SolicitanteCorreo ?? string.Empty).Trim().ToUpperInvariant() == correoNorm
                                         || (x?.Ap1Correo ?? string.Empty).Trim().ToUpperInvariant() == correoNorm
                                         || (x?.Ap2Correo ?? string.Empty).Trim().ToUpperInvariant() == correoNorm
                                         || (x?.Ap3Correo ?? string.Empty).Trim().ToUpperInvariant() == correoNorm)
                               .ToList();
                }

                if (!string.IsNullOrWhiteSpace(estadoNorm))
                {
                    list = list.Where(x => BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(x?.Estado) == estadoNorm)
                               .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ListarDesdeConsultasPorCorreo correo={Correo}", correo);
            }

            return list;
        }

        public async Task<List<Dictionary<string, object>>> ListarPendientesVpGh(string correoAprobador, string? estado = null)
        {
            var result = new List<Dictionary<string, object>>();
            try
            {
                await using var cn = new DB2Connection(_connectionString);
                await cn.OpenAsync();

                var estadoNorm = BackendRequisicionPersonal.Helpers.TextNormalizer.NormalizeForComparison(estado);
                var filtrarEstado = estadoNorm is "EN NOMINA" or "EN VP GH";

                var sql = @"
                    SELECT 
                        s.id,
                        s.tipo,
                        s.fecha_solicitud,
                        s.cargo_requerido,
                        s.estado,
                        s.nivel_aprobacion,
                        s.ap1_correo,
                        s.ap1_estado,
                        s.ap2_correo,
                        s.ap2_estado,
                        s.ap3_correo,
                        s.ap3_estado,
                        s.id_solicitante,
                        r.jefe_inmediato_solicitante
                    FROM solicitudes_aprobaciones_personal s
                    LEFT JOIN requisiciones_aprobaciones_personal r ON TRIM(r.identificacion) = TRIM(s.id_solicitante)
                    WHERE (@filtrar_estado = 0 AND UPPER(TRIM(s.estado)) = 'EN VP GH')
                       OR (@filtrar_estado = 1 AND UPPER(TRIM(s.estado)) = @estado)
                    ORDER BY s.id DESC";

                await using var cmd = new DB2Command(sql, cn);
                cmd.Parameters.Add(new DB2Parameter("@filtrar_estado", filtrarEstado ? 1 : 0));
                cmd.Parameters.Add(new DB2Parameter("@estado", (object?)(filtrarEstado ? estadoNorm : DBNull.Value) ?? DBNull.Value));
                await using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    var dict = new Dictionary<string, object>
                    {
                        ["id"] = r.GetInt32(0),
                        ["tipo"] = r.IsDBNull(1) ? "" : r.GetString(1).Trim(),
                        ["fecha_solicitud"] = r.IsDBNull(2) ? "" : r.GetString(2).Trim(),
                        ["cargo_requerido"] = r.IsDBNull(3) ? "" : r.GetString(3).Trim(),
                        ["estado"] = r.IsDBNull(4) ? "" : r.GetString(4).Trim(),
                        ["nivel_aprobacion"] = r.IsDBNull(5) ? "" : r.GetString(5).Trim(),
                        ["ap1_correo"] = r.IsDBNull(6) ? "" : r.GetString(6).Trim(),
                        ["ap1_estado"] = r.IsDBNull(7) ? "" : r.GetString(7).Trim(),
                        ["ap2_correo"] = r.IsDBNull(8) ? "" : r.GetString(8).Trim(),
                        ["ap2_estado"] = r.IsDBNull(9) ? "" : r.GetString(9).Trim(),
                        ["ap3_correo"] = r.IsDBNull(10) ? "" : r.GetString(10).Trim(),
                        ["ap3_estado"] = r.IsDBNull(11) ? "" : r.GetString(11).Trim(),
                        ["solicitante_id"] = r.IsDBNull(12) ? "" : r.GetString(12).Trim(),
                        ["solicitante_nombre"] = r.IsDBNull(13) ? "" : r.GetString(13).Trim()
                    };
                    result.Add(dict);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ListarPendientesVpGh correo={Correo}", correoAprobador);
            }
            return result;
        }

        public List<CargoCanal> ListarCargosCanales(string? area = null) 
            => _consultasService.ListarCargosCanales(area);

        public List<dynamic> ListarCargosAdministrativos(string? area) 
            => _consultasService.ListarCargosAdministrativos(area);

        public List<string> ListarCanales() 
            => _consultasService.ListarCanales();

        public bool AplicarAccion(int id, string estado, string? motivo = null, string? actorEmail = null, string? actorNombre = null) 
            => _aprobacionService.AplicarAccion(id, estado, motivo, actorEmail, actorNombre);

        public bool MarcarRevisadoRrhh(int id, string? identificacion = null) 
            => _aprobacionService.MarcarRevisadoRrhh(id, identificacion);

        public bool ActualizarEstadoEnVpGh(int id) 
            => _aprobacionService.ActualizarEstadoEnVpGh(id);

        public bool CerrarRequisicion(int id, string? identificacion = null) 
            => _aprobacionService.CerrarRequisicion(id, identificacion);

        public bool AprobarNomina(int id, string? actorNombre, string? actorEmail)
            => _aprobacionService.AprobarNomina(id, actorNombre, actorEmail);

        public bool RechazarNomina(int id, string? actorNombre, string? actorEmail, string? motivo)
            => _aprobacionService.RechazarNomina(id, actorNombre, actorEmail, motivo);

        // Nuevo wrapper para devolver de EN VP GH a EN NOMINA
        public bool DevolverAEnNomina(int id, string? actorNombre, string? actorEmail, string? motivo)
            => _aprobacionService.ReenviarAVpGhAEnNomina(id, actorNombre, actorEmail, motivo);

        public (string Asunto, string Html) ConstruirCorreoRequisicion(int id) 
            => _reportesService.ConstruirCorreoRequisicion(id);
    }
}
