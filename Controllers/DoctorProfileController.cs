using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Configuration;
using WebApplication1.Data;
using WebApplication2.Models;

public class DoctorProfileController : Controller
{
    private readonly ApplicationDbContext db;
    private readonly IConfiguration _configuration;

    public DoctorProfileController(ApplicationDbContext context, IConfiguration configuration)
    {
        db = context;
        _configuration = configuration;
    }


    public IActionResult TopRated()
    {
        var doctors = db.DoctorProfile

                              .OrderByDescending(d => d.Rating)
                              .ToList();

        return View(doctors);
    }

    public IActionResult GetPhoto(int id)
    {
        var doctor = db.DoctorProfile.FirstOrDefault(d => d.DoctorID == id);
        if (doctor?.Photo == null)
            return NotFound();

        return File(doctor.Photo, "image/jpeg");
    }
    public IActionResult Details(int id)
    {
        var doctor = db.DoctorProfile.FirstOrDefault(d => d.DoctorID == id);
        if (doctor == null)
        {
            return NotFound();
        }

        var reviews = db.Review
            .Where(r => r.DoctorID == id)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var viewModel = new DoctorProfileWithReviewsViewModel
        {
            Doctor = doctor,
            Reviews = reviews
        };

        return View(viewModel); 
    }

    [HttpPost]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
    {
        if (photo == null || photo.Length == 0)
        {
            return BadRequest("No file selected.");
        }

        byte[] photoBytes;
        using (var ms = new MemoryStream())
        {
            await photo.CopyToAsync(ms);
            photoBytes = ms.ToArray();
        }

    
        var doctor = db.DoctorProfile
            .FromSqlRaw("SELECT * FROM DoctorProfile WHERE DoctorID = {0}", id)
            .FirstOrDefault();

        if (doctor == null)
        {
            return NotFound();
        }

       
        var param = new[]
        {
        new SqlParameter("@Photo", photoBytes),
        new SqlParameter("@DoctorID", id)
        };

        db.Database.ExecuteSqlRaw("UPDATE DoctorProfile SET Photo = @Photo WHERE DoctorID = @DoctorID", param);

        return RedirectToAction("Details", new { id = id });
    }
    private readonly string connectionString = "Server=.;Database=MediTrackDBSystem;Trusted_Connection=True;TrustServerCertificate=True";

    public IActionResult GetDoctorPhoto(int id)
    {
        byte[] photoData = null;

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = "SELECT Photo FROM DoctorProfile WHERE DoctorID = @id";
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read() && !reader.IsDBNull(0))
                    {
                        photoData = (byte[])reader["Photo"];
                    }
                }
            }
        }

        if (photoData != null)
        {
            return File(photoData, "image/png"); 
        }

        return NotFound(); 
    }
    public IActionResult Search(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return View(new List<DoctorProfile>());
        }

        List<DoctorProfile> results = new List<DoctorProfile>();

        using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            conn.Open();

            string sql = @"
            SELECT * 
            FROM DoctorProfile
            WHERE DoctorName LIKE '%' + @query + '%' 
               OR Bio LIKE '%' + @query + '%'";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@query", query);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new DoctorProfile
                        {
                            DoctorID = reader.GetInt32(reader.GetOrdinal("DoctorID")),
                            DoctorName = reader.GetString(reader.GetOrdinal("DoctorName")),
                            Bio = reader.GetString(reader.GetOrdinal("Bio")),
                            Photo = reader["Photo"] as byte[],
                            ClinicAddress = reader.GetString(reader.GetOrdinal("ClinicAddress")),
                            AvailableDays = reader.GetString(reader.GetOrdinal("AvailableDays")),
                            UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                            SpecialtyID = reader.GetInt32(reader.GetOrdinal("SpecialtyID"))
                        });
                    }
                }
            }
        }

        return View(results);
    }



    [HttpPost]
    public IActionResult UpdateAvailableDays(int DoctorID, string[] SelectedDays)
    {
        string days = string.Join(",", SelectedDays);

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = "UPDATE DoctorProfile SET AvailableDays = @days WHERE DoctorID = @id";
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@days", days);
                cmd.Parameters.AddWithValue("@id", DoctorID);
                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Details", new { id = DoctorID });

    }


    [HttpGet]
    public IActionResult EditAvailableDays(int id)
    {
        var role = HttpContext.Session.GetString("Role");
        var currentDoctorId = HttpContext.Session.GetInt32("DoctorID");

        if (
            (role == "Doctor" && HttpContext.Session.GetInt32("DoctorID") != id)
            &&
            role != "Admin"
        )
        {
            return Unauthorized(); 
        }

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = "SELECT DoctorID, AvailableDays FROM DoctorProfile WHERE DoctorID = @id";
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var model = new DoctorProfile
                        {
                            DoctorID = reader.GetInt32(0),
                            AvailableDays = reader.IsDBNull(1) ? "" : reader.GetString(1)
                        };
                        return View(model);
                    }
                }
            }
        }

        return NotFound();
    }
    [HttpPost]
    public IActionResult UpdateBio(int DoctorID, string Bio)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserID");
        string role = HttpContext.Session.GetString("Role");

        using (var conn = new SqlConnection("Server=.;Database=MediTrackDBSystem;Trusted_Connection=True;TrustServerCertificate=True"))
        {
            conn.Open();

            string checkSql = "SELECT UserID FROM DoctorProfile WHERE DoctorID = @id";
            using (var cmd = new SqlCommand(checkSql, conn))
            {
                cmd.Parameters.AddWithValue("@id", DoctorID);
                var result = cmd.ExecuteScalar();

                if (result == null || Convert.ToInt32(result) != currentUserId || role != "Doctor")
                    return Unauthorized();
            }

            string updateSql = "UPDATE DoctorProfile SET Bio = @bio WHERE DoctorID = @id";
            using (var cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@bio", Bio);
                cmd.Parameters.AddWithValue("@id", DoctorID);
                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Details", new { id = DoctorID });
    }
    [HttpPost]
    [HttpPost]
    public IActionResult UpdateDoctorInfo(int DoctorID, string DoctorName, string Bio, string ClinicAddress, string AvailableDays)
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = @"UPDATE DoctorProfile 
                       SET DoctorName = @DoctorName,
                           Bio = @Bio,
                           ClinicAddress = @Clinic,
                           AvailableDays = @Days
                       WHERE DoctorID = @id";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@DoctorName", DoctorName);
                cmd.Parameters.AddWithValue("@Bio", Bio);
                cmd.Parameters.AddWithValue("@Clinic", ClinicAddress);
                cmd.Parameters.AddWithValue("@Days", AvailableDays);
          
                cmd.Parameters.AddWithValue("@id", DoctorID);
                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Details", new { id = DoctorID });
    }

    public IActionResult MyPatients(DateTime? startDate, DateTime? endDate)
    {
        var doctorUserId = HttpContext.Session.GetInt32("UserID");

        if (doctorUserId == null || HttpContext.Session.GetString("Role") != "Doctor")
            return Unauthorized();

        List<AppUser> patients = new List<AppUser>();

        using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            conn.Open();

            string getDoctorIdSql = "SELECT DoctorID FROM DoctorProfile WHERE UserID = @UserID";
            int doctorId;

            using (SqlCommand cmd = new SqlCommand(getDoctorIdSql, conn))
            {
                cmd.Parameters.AddWithValue("@UserID", doctorUserId);
                var result = cmd.ExecuteScalar();
                if (result == null)
                    return NotFound("Doctor not found.");

                doctorId = Convert.ToInt32(result);
            }

            string getPatientsSql = @"
            SELECT DISTINCT U.UserID, U.FullName, U.Email, U.Phone
            FROM Appointment A
            JOIN PatientProfile P ON A.PatientID = P.PatientID
            JOIN AppUser U ON P.UserID = U.UserID
            WHERE A.DoctorID = @DoctorID";

            if (startDate.HasValue && endDate.HasValue)
            {
                getPatientsSql += " AND A.DateTime BETWEEN @StartDate AND @EndDate";
            }

            using (SqlCommand cmd = new SqlCommand(getPatientsSql, conn))
            {
                cmd.Parameters.AddWithValue("@DoctorID", doctorId);

                if (startDate.HasValue && endDate.HasValue)
                {
                    cmd.Parameters.AddWithValue("@StartDate", startDate.Value.Date);
                    cmd.Parameters.AddWithValue("@EndDate", endDate.Value.Date.AddDays(1).AddSeconds(-1));
                }

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        patients.Add(new AppUser
                        {
                            UserID = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            Email = reader.GetString(2),
                            Phone = reader.IsDBNull(3) ? "" : reader.GetString(3)
                        });
                    }
                }
            }
        }

        return View(patients);
    }

    public IActionResult PatientAppointments(int patientId)
    {
        var doctorUserId = HttpContext.Session.GetInt32("UserID");
        if (doctorUserId == null || HttpContext.Session.GetString("Role") != "Doctor")
            return Unauthorized();

        List<Appointment> appointments = new List<Appointment>();

        using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            conn.Open();

            string sql = @"
            SELECT A.*
            FROM Appointment A
            INNER JOIN DoctorProfile D ON A.DoctorID = D.DoctorID
            INNER JOIN PatientProfile P ON A.PatientID = P.PatientID
            WHERE D.UserID = @DoctorUserID
              AND P.UserID = @PatientUserID";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@DoctorUserID", doctorUserId);
                cmd.Parameters.AddWithValue("@PatientUserID", patientId);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        appointments.Add(new Appointment
                        {
                            AppointmentID = reader.GetInt32(reader.GetOrdinal("AppointmentID")),
                            DoctorID = reader.GetInt32(reader.GetOrdinal("DoctorID")),
                            PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                            DateTime = reader.GetDateTime(reader.GetOrdinal("DateTime")),
                            Reason = reader.GetString(reader.GetOrdinal("Reason")),
                            Status = reader.GetString(reader.GetOrdinal("Status"))
                        });
                    }
                }
            }
        }

        return View(appointments);
    }


    public virtual ICollection<Review> Reviews { get; set; }

    [NotMapped]
    public double AverageRating => Reviews != null && Reviews.Any()
        ? Reviews.Average(r => r.Rating)
        : 0;

}
