using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using WebApplication1.Data;
using WebApplication2.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Numerics;

namespace WebApplication2.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            string token = null;
            int userId = 0;
            string userRole = "";

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string userSql = "SELECT UserID FROM [AppUser] WHERE Email = @Email AND PasswordHash = @Password";
                using (SqlCommand cmd = new SqlCommand(userSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@Password", password);

                    var result = cmd.ExecuteScalar();
                    if (result == null)
                    {
                        ViewBag.Error = "Invalid email or password.";
                        return View();
                    }

                    userId = Convert.ToInt32(result);
                }

                token = Guid.NewGuid().ToString();
                string sessionSql = @"INSERT INTO LoginSession (UserID, Token, CreatedAt, ExpiresAt)
                                      VALUES (@UserID, @Token, @CreatedAt, @ExpiresAt)";

                using (SqlCommand cmd = new SqlCommand(sessionSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    cmd.Parameters.AddWithValue("@Token", token);
                    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                    cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.Now.AddDays(1));

                    cmd.ExecuteNonQuery();
                }

                string roleSql = "SELECT Role FROM [AppUser] WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(roleSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    userRole = (string)cmd.ExecuteScalar();
                }

                if (userRole == "Doctor")
                {
                    string doctorSql = "SELECT DoctorID FROM DoctorProfile WHERE UserID = @uid";
                    using (SqlCommand cmd = new SqlCommand(doctorSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@uid", userId);
                        var docResult = cmd.ExecuteScalar();
                        if (docResult != null)
                        {
                            int doctorId = Convert.ToInt32(docResult);
                            HttpContext.Session.SetInt32("DoctorID", doctorId);
                        }
                    }
                }
            }

            HttpContext.Session.SetString("Token", token);
            HttpContext.Session.SetString("Role", userRole);
            HttpContext.Session.SetInt32("UserID", userId);      

            return RedirectToAction("IndexUser", "Home");
        }

        public IActionResult AdminOnlyPage()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
            {
                return Unauthorized();
            }

            return View();
        }


        [HttpPost]
        public IActionResult Register(RegisterViewModel model)

        {
            int newUserId;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string checkEmailSql = "SELECT COUNT(*) FROM [AppUser] WHERE Email = @Email";
                using (SqlCommand checkCmd = new SqlCommand(checkEmailSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", model.Email);
                    int count = (int)checkCmd.ExecuteScalar();
                    if (count > 0)
                    {
                        LoadSpecialties(); 
                        ViewBag.Error = "❌ البريد الإلكتروني مستخدم من قبل.";
                        return View(model);
                    }
                }

                string insertUserSql = @"
INSERT INTO [AppUser] (FullName, Email, PasswordHash, Phone, Role)
OUTPUT INSERTED.UserID
VALUES (@FullName, @Email, @PasswordHash, @Phone, @Role)";

                using (SqlCommand insertCmd = new SqlCommand(insertUserSql, conn))
                {
                    insertCmd.Parameters.AddWithValue("@FullName", model.FullName);
                    insertCmd.Parameters.AddWithValue("@Email", model.Email);
                    insertCmd.Parameters.AddWithValue("@PasswordHash", model.PasswordHash);
                    insertCmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Role", model.Role);
                    newUserId = (int)insertCmd.ExecuteScalar();
                }

                byte[] photoBytes = null;
               
                if (model.PhotoFile != null)
                {
                    using (var ms = new MemoryStream())
                    {
                        model.PhotoFile.CopyTo(ms);
                        photoBytes = ms.ToArray();
                    }
                }


                if (model.Role == "Patient")
                {
                    string insertPatientSql = @"
                INSERT INTO PatientProfile (UserID, DateOfBirth, Gender, Address, MedicalFileNumber)
                VALUES (@UserID, @DOB, @Gender, @Address, @FileNumber)";
                    using (SqlCommand cmd = new SqlCommand(insertPatientSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", newUserId);
                        cmd.Parameters.AddWithValue("@DOB", (object?)model.DateOfBirth ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Gender", (object?)model.Gender ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Address", (object?)model.Address ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FileNumber", "MF" + newUserId);
                        cmd.ExecuteNonQuery();
                    }
                }
                else if (model.Role == "Doctor")
                {
                    model.Rating = 4; 

                    string insertDoctorSql = @"
    INSERT INTO DoctorProfile (UserID, DoctorName, Bio, Rating, AvailableDays, ClinicAddress, Photo, SpecialtyID)
    VALUES (@UserID, @DoctorName, @Bio, @Rating, @Days, @Clinic, @Photo, @SpecialtyID)";
                    using (SqlCommand cmd = new SqlCommand(insertDoctorSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", newUserId);
                        cmd.Parameters.AddWithValue("@DoctorName", model.FullName);
                        cmd.Parameters.AddWithValue("@Bio", (object?)model.Bio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Rating", model.Rating); 
                        cmd.Parameters.AddWithValue("@Days", (object?)model.AvailableDays ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Clinic", (object?)model.ClinicAddress ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Photo", (object?)photoBytes ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@SpecialtyID", (object?)model.SpecialtyID ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

            }

            return RedirectToAction("Login");
        }
        private void LoadSpecialties()
        {
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                string sql = "SELECT SpecialtyID, Name FROM Specialty";
                var list = new List<Specialty>();

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new Specialty
                        {
                            SpecialtyID = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }

                ViewBag.Specialties = list;
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            LoadSpecialties();
            return View();
        }

        public IActionResult DoctorsWithSpecialties()
        {
            var results = new List<DoctorWithSpecialtyViewModel>();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                string sql = @"
            SELECT a.FullName, s.Name AS Specialty, d.ClinicAddress
            FROM DoctorProfile d
            JOIN AppUser a ON d.UserID = a.UserID
            JOIN Specialty s ON d.SpecialtyID = s.SpecialtyID";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new DoctorWithSpecialtyViewModel
                        {
                            DoctorName = reader["FullName"].ToString(),
                            SpecialtyName = reader["Specialty"].ToString(),
                            ClinicAddress = reader["ClinicAddress"].ToString()
                        });
                    }
                }
            }

            return View(results);
        }


        public IActionResult Logout()
        {
            var token = HttpContext.Session.GetString("Token");

            if (!string.IsNullOrEmpty(token))
            {
                using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    string deleteSql = "DELETE FROM LoginSession WHERE Token = @Token";

                    using (SqlCommand cmd = new SqlCommand(deleteSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Token", token);
                        cmd.ExecuteNonQuery();
                    }
                }

                HttpContext.Session.Clear();
            }

            return RedirectToAction("Login");
        }
        public IActionResult AllUsers()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return Unauthorized(); 

            List<AppUser> users = new List<AppUser>();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                string sql = "SELECT * FROM AppUser";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        users.Add(new AppUser
                        {
                            UserID = Convert.ToInt32(reader["UserID"]),
                            FullName = reader["FullName"].ToString(),
                            Email = reader["Email"].ToString(),
                            Phone = reader["Phone"]?.ToString(),
                            Role = reader["Role"].ToString()
                        });
                    }
                }
            }

            return View(users);
        }
        [HttpPost]
        public IActionResult DeleteUser(int id)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    string deleteNotes = @"
                DELETE FROM MedicalNote 
                WHERE AppointmentID IN (
                    SELECT AppointmentID FROM Appointment WHERE PatientID = @id
                )";
                    using (SqlCommand cmd = new SqlCommand(deleteNotes, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    using (SqlCommand cmd = new SqlCommand("DELETE FROM Review WHERE PatientID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    using (SqlCommand cmd = new SqlCommand("DELETE FROM Appointment WHERE PatientID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int rows = cmd.ExecuteNonQuery();
                        TempData["Success"] = $"✅ تم حذف {rows} موعدًا مرتبطًا بالمريض.";
                    }

                    using (SqlCommand cmd = new SqlCommand("DELETE FROM LoginSession WHERE UserID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    using (SqlCommand cmd = new SqlCommand("DELETE FROM Notification WHERE UserID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    using (SqlCommand cmd = new SqlCommand("DELETE FROM PatientProfile WHERE UserID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    using (SqlCommand cmd = new SqlCommand("DELETE FROM AppUser WHERE UserID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["Success"] += "\n✅ تم حذف المريض بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "❌ فشل الحذف: " + ex.Message;
            }

            return RedirectToAction("AllUsers");
        }

        [HttpPost]
        public IActionResult UpdateUser(int UserID, string FullName, string Phone, string Role)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return Unauthorized();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                string sql = @"UPDATE AppUser 
                       SET FullName = @FullName, Phone = @Phone, Role = @Role 
                       WHERE UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", UserID);
                    cmd.Parameters.AddWithValue("@FullName", FullName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(Phone) ? DBNull.Value : Phone);
                    cmd.Parameters.AddWithValue("@Role", Role ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("AllUsers");
        }
        public IActionResult MyAccount()
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            var role = HttpContext.Session.GetString("Role");

            if (userId == null)
                return RedirectToAction("Login");

            AppUser user = null;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string userSql = "SELECT UserID, FullName, Email, Phone, Role FROM AppUser WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(userSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            user = new AppUser
                            {
                                UserID = reader.GetInt32(0),
                                FullName = reader.GetString(1),
                                Email = reader.GetString(2),
                                Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Role = reader.GetString(4)
                            };
                        }
                        else
                        {
                            return NotFound("User not found.");
                        }
                    }
                }

                ViewBag.Role = role;

                if (role == "Patient")
                {
                    string patientSql = "SELECT * FROM PatientProfile WHERE UserID = @UserID";
                    using (SqlCommand cmd = new SqlCommand(patientSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var patient = new PatientProfile
                                {
                                    PatientID = reader.GetInt32(0),
                                    UserID = reader.GetInt32(1),
                                    DateOfBirth = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                                    Gender = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    Address = reader.IsDBNull(4) ? null : reader.GetString(4),
                                    MedicalFileNumber = reader.IsDBNull(5) ? null : reader.GetString(5)
                                };

                                ViewBag.PatientProfile = patient;
                            }
                        }
                    }
                }
                else if (role == "Doctor")
                {
                    string doctorSql = "SELECT * FROM DoctorProfile WHERE UserID = @UserID";
                    using (SqlCommand cmd = new SqlCommand(doctorSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var doctor = new DoctorProfile
                                {
                                    DoctorID = reader.GetInt32(0),
                                    SpecialtyID = reader.GetInt32(1),
                                    DoctorName = reader.GetString(2),
                                    Bio = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    Rating = reader.GetDecimal(4),
                                    AvailableDays = reader.IsDBNull(5) ? null : reader.GetString(5),
                                    ClinicAddress = reader.IsDBNull(6) ? null : reader.GetString(6),
                                    Photo = reader.IsDBNull(7) ? null : (byte[])reader["Photo"],
                                    UserID = reader.GetInt32(8)
                                };

                                ViewBag.DoctorProfile = doctor;
                            }
                        }
                    }
                }
            }

            return View(user);
        }

        [HttpPost]
        public IActionResult EditAccount(AppUser model, string DateOfBirth, string Gender, string Address, string Bio, string AvailableDays, string ClinicAddress)
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            var role = HttpContext.Session.GetString("Role");

            if (userId == null || userId != model.UserID)
                return Unauthorized();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string updateUserSql = @"
            UPDATE AppUser
            SET FullName = @FullName,
                Email = @Email,
                Phone = @Phone
            WHERE UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(updateUserSql, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", model.FullName);
                    cmd.Parameters.AddWithValue("@Email", model.Email);
                    cmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserID", model.UserID);
                    cmd.ExecuteNonQuery();
                }

                if (role == "Patient")
                {
                    string updatePatientSql = @"
                UPDATE PatientProfile
                SET DateOfBirth = @DOB,
                    Gender = @Gender,
                    Address = @Address
                WHERE UserID = @UserID";

                    using (SqlCommand cmd = new SqlCommand(updatePatientSql, conn))
                    {
                        if (DateTime.TryParse(DateOfBirth, out var dob))
                            cmd.Parameters.AddWithValue("@DOB", dob);
                        else
                            cmd.Parameters.AddWithValue("@DOB", DBNull.Value);

                        cmd.Parameters.AddWithValue("@Gender", (object?)Gender ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Address", (object?)Address ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        cmd.ExecuteNonQuery();
                    }
                }

                else if (role == "Doctor")
                {
                    string updateDoctorSql = @"
                UPDATE DoctorProfile
                SET Bio = @Bio,
                    AvailableDays = @AvailableDays,
                    ClinicAddress = @ClinicAddress
                WHERE UserID = @UserID";

                    using (SqlCommand cmd = new SqlCommand(updateDoctorSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Bio", (object?)Bio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@AvailableDays", (object?)AvailableDays ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClinicAddress", (object?)ClinicAddress ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("MyAccount");
        }

        [HttpGet]
        public IActionResult EditAccount()
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            var role = HttpContext.Session.GetString("Role");

            if (userId == null)
                return RedirectToAction("Login");

            AppUser user = null;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string userSql = "SELECT UserID, FullName, Email, Phone, Role FROM AppUser WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(userSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            user = new AppUser
                            {
                                UserID = reader.GetInt32(0),
                                FullName = reader.GetString(1),
                                Email = reader.GetString(2),
                                Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Role = reader.GetString(4)
                            };
                        }
                        else
                        {
                            return NotFound();
                        }
                    }
                }

                if (role == "Patient")
                {
                    string patientSql = "SELECT * FROM PatientProfile WHERE UserID = @UserID";
                    using (SqlCommand cmd = new SqlCommand(patientSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var patient = new PatientProfile
                                {
                                    PatientID = reader.GetInt32(0),
                                    UserID = reader.GetInt32(1),
                                    DateOfBirth = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                                    Gender = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    Address = reader.IsDBNull(4) ? null : reader.GetString(4),
                                    MedicalFileNumber = reader.IsDBNull(5) ? null : reader.GetString(5)
                                };
                                ViewBag.Patient = patient;
                            }
                        }
                    }
                }
                else if (role == "Doctor")
                {
                    string doctorSql = "SELECT * FROM DoctorProfile WHERE UserID = @UserID";
                    using (SqlCommand cmd = new SqlCommand(doctorSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var doctor = new DoctorProfile
                                {
                                    DoctorID = reader.GetInt32(0),
                                    SpecialtyID = reader.GetInt32(1),
                                    DoctorName = reader.GetString(2),
                                    Bio = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    Rating = reader.GetDecimal(4),
                                    AvailableDays = reader.IsDBNull(5) ? null : reader.GetString(5),
                                    ClinicAddress = reader.IsDBNull(6) ? null : reader.GetString(6),
                                    Photo = reader.IsDBNull(7) ? null : (byte[])reader["Photo"],
                                    UserID = reader.GetInt32(8)
                                };
                                ViewBag.Doctor = doctor;
                            }
                        }
                    }
                }
            }

            ViewBag.Role = role;
            return View(user);
        }

    }
}