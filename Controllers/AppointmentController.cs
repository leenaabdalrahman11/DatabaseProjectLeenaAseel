﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using WebApplication2.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace WebApplication2.Controllers
{
    public class AppointmentController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public AppointmentController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }
        public IActionResult Index()
        {
            List<Appointment> appointments = new List<Appointment>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM Appointment";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        appointments.Add(new Appointment
                        {
                            AppointmentID = Convert.ToInt32(reader["AppointmentID"]),
                            DoctorID = Convert.ToInt32(reader["DoctorID"]),
                            PatientID = Convert.ToInt32(reader["PatientID"]),
                            DateTime = Convert.ToDateTime(reader["DateTime"]),
                            Reason = reader["Reason"].ToString(),
                            Status = reader["Status"].ToString()
                        });
                    }
                }
            }

            return View(appointments);
        }

        [HttpGet]
        public IActionResult Create(int doctorId)
        {
            int? patientUserId = HttpContext.Session.GetInt32("UserID");
            if (patientUserId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            int patientId = 0;
            string doctorName = "";

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // ✅ جلب PatientID من جدول PatientProfile باستخدام UserID
                string getPatientSql = "SELECT PatientID FROM PatientProfile WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(getPatientSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", patientUserId.Value);
                    object result = cmd.ExecuteScalar();
                    if (result == null)
                    {
                        TempData["Error"] = "لم يتم العثور على المريض.";
                        return RedirectToAction("Login", "Auth");
                    }
                    patientId = Convert.ToInt32(result);
                }

                // ✅ جلب اسم الطبيب
                string getDoctorSql = "SELECT DoctorName FROM DoctorProfile WHERE DoctorID = @DoctorID";
                using (SqlCommand cmd = new SqlCommand(getDoctorSql, conn))
                {
                    cmd.Parameters.AddWithValue("@DoctorID", doctorId);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        doctorName = result.ToString();
                    }
                }
            }

            ViewBag.DoctorName = doctorName;

            return View(new Appointment
            {
                DoctorID = doctorId,
                PatientID = patientId,
                Status = "booked"
            });
        }


        [HttpPost]
        public IActionResult Create(int DoctorID, string SelectedDay, DateTime DateTime, string Reason)
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null)
                return Content("❌ User not logged in.");

            string shortDay = SelectedDay switch
            {
                "Monday" => "Mon",
                "Tuesday" => "Tue",
                "Wednesday" => "Wed",
                "Thursday" => "Thu",
                "Friday" => "Fri",
                "Saturday" => "Sat",
                "Sunday" => "Sun",
                _ => null
            };

            if (shortDay == null)
                return Content("❌ اليوم المحدد غير صالح.");

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                int patientId;
                string getPatientIdSql = "SELECT PatientID FROM PatientProfile WHERE UserID = @uid";
                using (SqlCommand getCmd = new SqlCommand(getPatientIdSql, conn))
                {
                    getCmd.Parameters.AddWithValue("@uid", userId);
                    var result = getCmd.ExecuteScalar();
                    if (result == null)
                        return Content("❌ لم يتم العثور على ملف المريض.");
                    patientId = Convert.ToInt32(result);
                }

                string insertSql = @"INSERT INTO Appointment (DoctorID, PatientID, DateTime, Reason, Status)
                             VALUES (@DoctorID, @PatientID, @DateTime, @Reason, @Status)";
                using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@DoctorID", DoctorID);
                    cmd.Parameters.AddWithValue("@PatientID", patientId);
                    cmd.Parameters.AddWithValue("@DateTime", DateTime);
                    cmd.Parameters.AddWithValue("@Reason", Reason);
                    cmd.Parameters.AddWithValue("@Status", "booked");
                    cmd.ExecuteNonQuery();
                }

                string getDaysSql = "SELECT AvailableDays FROM DoctorProfile WHERE DoctorID = @DoctorID";
                string availableDays = null;

                using (SqlCommand getCmd = new SqlCommand(getDaysSql, conn))
                {
                    getCmd.Parameters.AddWithValue("@DoctorID", DoctorID);
                    var result = getCmd.ExecuteScalar();
                    availableDays = result?.ToString();
                }

                if (!string.IsNullOrEmpty(availableDays))
                {
                    var days = availableDays.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    days.RemoveAll(d => d.Equals(shortDay, StringComparison.OrdinalIgnoreCase));
                    string updatedDays = string.Join(",", days);

                    string updateSql = "UPDATE DoctorProfile SET AvailableDays = @UpdatedDays WHERE DoctorID = @DoctorID";
                    using (SqlCommand updateCmd = new SqlCommand(updateSql, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@UpdatedDays", updatedDays);
                        updateCmd.Parameters.AddWithValue("@DoctorID", DoctorID);
                        updateCmd.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("MyAppointments");
        }

        public IActionResult MyAppointments()
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null)
                return RedirectToAction("Login", "Auth");

            var appointments = new List<Appointment>();
            int patientId;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string getPatientIdSql = "SELECT PatientID FROM PatientProfile WHERE UserID = @uid";
                using (SqlCommand cmd = new SqlCommand(getPatientIdSql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    var result = cmd.ExecuteScalar();
                    if (result == null)
                        return Content("❌ لم يتم العثور على ملف المريض.");
                    patientId = Convert.ToInt32(result);
                }

                string sql = "SELECT * FROM Appointment WHERE PatientID = @id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", patientId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            appointments.Add(new Appointment
                            {
                                DoctorID = reader.GetInt32(0),
                                PatientID = reader.GetInt32(1),
                                DateTime = reader.GetDateTime(2),
                                Reason = reader.GetString(3),
                                Status = reader.GetString(4),
                                AppointmentID = reader.GetInt32(5)
                            });
                        }
                    }
                }
            }

            return View(appointments);
        }

        private int GetUserIdFromToken(string token)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT UserID FROM LoginSession WHERE Token = @token AND ExpiresAt > GETDATE()";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@token", token);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
            }
            return 0;
        }
        [HttpPost]
        public IActionResult BookSimple(string DoctorName, DateTime DateTime, string Reason)
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null)
                return Content("❌ You must log in first..");

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string getDoctorSql = "SELECT DoctorID FROM DoctorProfile WHERE DoctorName = @name";
                int doctorId;

                using (SqlCommand cmd = new SqlCommand(getDoctorSql, conn))
                {
                    cmd.Parameters.AddWithValue("@name", DoctorName);
                    var result = cmd.ExecuteScalar();
                    if (result == null)
                        return Content("❌The specified doctor was not found.");
                    doctorId = Convert.ToInt32(result);
                }

                int patientId;
                string getPatientIdSql = "SELECT PatientID FROM PatientProfile WHERE UserID = @uid";
                using (SqlCommand cmd = new SqlCommand(getPatientIdSql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    var result = cmd.ExecuteScalar();
                    if (result == null)
                        return Content("❌No patient file found. Please create a patient account first.");
                    patientId = Convert.ToInt32(result);
                }

                string insertSql = @"
            INSERT INTO Appointment (DoctorID, PatientID, DateTime, Reason, Status)
            VALUES (@DoctorID, @PatientID, @DateTime, @Reason, 'booked')";
                using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@DoctorID", doctorId);
                    cmd.Parameters.AddWithValue("@PatientID", patientId); 
                    cmd.Parameters.AddWithValue("@DateTime", DateTime);
                    cmd.Parameters.AddWithValue("@Reason", Reason);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("MyAppointments");
        }

        [HttpPost]
        public IActionResult Delete(int appointmentId)
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null)
                return RedirectToAction("Login", "Auth");

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = "DELETE FROM Appointment WHERE AppointmentID = @id AND PatientID IN (SELECT PatientID FROM PatientProfile WHERE UserID = @uid)";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", appointmentId);
                    cmd.Parameters.AddWithValue("@uid", userId);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("MyAppointments");
        }



    }
}
