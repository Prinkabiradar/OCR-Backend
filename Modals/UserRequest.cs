namespace OCR_BACKEND.Modals
{
    public class UserRequest
    {
        public int UserId { get; set; } // 0 = insert
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string Mobile { get; set; }
        public string Email { get; set; }
        public string UserName { get; set; }
        public string UserPass { get; set; }
        public int RoleId { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
    }
}
