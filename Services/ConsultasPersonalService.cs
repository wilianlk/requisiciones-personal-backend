using System;
using System.Collections.Generic;
using BackendRequisicionPersonal.Models;
using IBM.Data.Db2;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Services
{
    public class ConsultasPersonalService
    {
        private readonly string _connectionString;
        private readonly ILogger<ConsultasPersonalService> _logger;

        public ConsultasPersonalService(
            ILogger<ConsultasPersonalService> logger,
            string connectionString)
        {
            _logger = logger;
            _connectionString = connectionString;
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
    }
}
