using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication2.Models
{
    [Table("PatientProfile")]
    public class PatientProfile
    {
        [Key]
        public int PatientID { get; set; }

        [ForeignKey("AppUser")]
        public int UserID { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string Gender { get; set; }

        public string Address { get; set; }

        public string MedicalFileNumber { get; set; }

        public AppUser AppUser { get; set; }
    }
}
