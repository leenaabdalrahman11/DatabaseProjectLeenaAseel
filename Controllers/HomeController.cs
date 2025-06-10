using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection.PortableExecutable;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    public class HomeController : Controller
    {

    
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext dp;
        private readonly IConfiguration _configuration;



        public HomeController(ILogger<HomeController> logger, ApplicationDbContext dp, IConfiguration configuration)
        {
            _logger = logger;
            this.dp = dp;
            _configuration = configuration;
        }


        public IActionResult Index()
        {
            var token = HttpContext.Session.GetString("Token");

            if (string.IsNullOrEmpty(token))
            {
                return View("IndexGuest"); 
            }

            return RedirectToAction("IndexUser");
        }

        public IActionResult IndexUser()
        {
            var token = HttpContext.Session.GetString("Token");
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            List<DoctorProfile> doctors = new List<DoctorProfile>();
            List<Review> reviews = new List<Review>();
            List<Specialty> specialties = new List<Specialty>();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // 1. جلب الأطباء مع متوسط التقييم
                string sqlDoctors = @"
SELECT 
    DP.DoctorID, DP.DoctorName, DP.Bio, DP.Photo, DP.ClinicAddress,
    DP.AvailableDays, DP.UserID, DP.SpecialtyID,
    ISNULL(AVG(R.Rating), 0) AS AverageRating
FROM DoctorProfile DP
LEFT JOIN Review R ON DP.DoctorID = R.DoctorID
GROUP BY 
    DP.DoctorID, DP.DoctorName, DP.Bio, DP.Photo, DP.ClinicAddress,
    DP.AvailableDays, DP.UserID, DP.SpecialtyID
ORDER BY AverageRating DESC
";

                using (SqlCommand cmd = new SqlCommand(sqlDoctors, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        doctors.Add(new DoctorProfile
                        {
                            DoctorID = reader.GetInt32(reader.GetOrdinal("DoctorID")),
                            DoctorName = reader.GetString(reader.GetOrdinal("DoctorName")),
                            Bio = reader.GetString(reader.GetOrdinal("Bio")),
                            Photo = reader["Photo"] as byte[],
                            ClinicAddress = reader.GetString(reader.GetOrdinal("ClinicAddress")),
                            AvailableDays = reader.GetString(reader.GetOrdinal("AvailableDays")),
                            UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                            SpecialtyID = reader.GetInt32(reader.GetOrdinal("SpecialtyID")),
                            AverageRating = Convert.ToDouble(reader["AverageRating"])  
                        });

                    }
                }

                // 2. جلب أحدث 10 مراجعات مع اسم المريض
                string sqlReviews = @"
SELECT TOP 10 R.*, 
       U.FullName, U.Email, 
       D.DoctorName
FROM Review R
INNER JOIN PatientProfile P ON R.PatientID = P.PatientID
INNER JOIN AppUser U ON P.UserID = U.UserID
INNER JOIN DoctorProfile D ON R.DoctorID = D.DoctorID
ORDER BY R.CreatedAt DESC;
";
                using (SqlCommand cmd = new SqlCommand(sqlReviews, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        reviews.Add(new Review
                        {
                            ReviewID = reader.GetInt32(reader.GetOrdinal("ReviewID")),
                            DoctorID = reader.GetInt32(reader.GetOrdinal("DoctorID")),
                            PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                            Rating = reader.GetInt32(reader.GetOrdinal("Rating")),
                            Comment = reader.GetString(reader.GetOrdinal("Comment")),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

                            Patient = new PatientProfile
                            {
                                AppUser = new AppUser
                                {
                                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                                    Email = reader.GetString(reader.GetOrdinal("Email"))
                                }
                            },

                            DoctorProfile = new DoctorProfile
                            {
                                DoctorName = reader.GetString(reader.GetOrdinal("DoctorName"))
                            }
                        });



                    }
                }

                // 3. جلب التخصصات
                string sqlSpecialties = "SELECT * FROM Specialty";

                using (SqlCommand cmd = new SqlCommand(sqlSpecialties, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        specialties.Add(new Specialty
                        {
                            SpecialtyID = reader.GetInt32(reader.GetOrdinal("SpecialtyID")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Description = reader.GetString(reader.GetOrdinal("Description")),
                            Photo = reader["Photo"] as byte[]
                        });
                    }
                }
            }

            var viewModel = new HomePageViewModel
            {
                Doctors = doctors,
                Specialties = specialties,
                Reviews = reviews
            };

            return View(viewModel);
        }


        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
