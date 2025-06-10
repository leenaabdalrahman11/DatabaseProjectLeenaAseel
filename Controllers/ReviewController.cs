using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WebApplication1.Data;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public ReviewController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration; 
        }

        [HttpPost]
        public IActionResult AddReview(Review review)
        {
            if (HttpContext.Session.GetString("Role") != "Patient")
                return Unauthorized();

            var userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null)
                return RedirectToAction("Login", "Auth");

            int patientId;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // 1. جلب PatientID من جدول PatientProfile
                string getPatientIdSql = "SELECT PatientID FROM PatientProfile WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(getPatientIdSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId.Value);
                    object result = cmd.ExecuteScalar();
                    if (result == null)
                    {
                        TempData["Error"] = "⚠️ No patient file found.";
                        return RedirectToAction("Login", "Auth");
                    }

                    patientId = Convert.ToInt32(result);
                }

                // 2. إدخال التقييم الجديد
                string insertSql = @"
            INSERT INTO Review (PatientID, DoctorID, Rating, Comment, CreatedAt)
            VALUES (@PatientID, @DoctorID, @Rating, @Comment, @CreatedAt)";

                using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@PatientID", patientId);
                    cmd.Parameters.AddWithValue("@DoctorID", review.DoctorID);
                    cmd.Parameters.AddWithValue("@Rating", review.Rating);
                    cmd.Parameters.AddWithValue("@Comment", review.Comment ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Details", "DoctorProfile", new { id = review.DoctorID });
        }

        [HttpPost]
        public IActionResult DeleteReview(int ReviewID)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "Admin")
                return Unauthorized();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                string deleteSql = "DELETE FROM Review WHERE ReviewID = @id";
                using (SqlCommand cmd = new SqlCommand(deleteSql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", ReviewID);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Message"] = "✅ Comment deleted successfully.";
            return Redirect(Request.Headers["Referer"].ToString());
        }



    }
}