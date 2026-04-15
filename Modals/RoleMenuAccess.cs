namespace OCR_BACKEND.Modals
{
    public class RoleMenuAccess
    {
        public int RoleAccessId { get; set; }
        public int RoleId { get; set; }
        public int MenuId { get; set; }
        public bool CanView { get; set; }
        public bool CanInsert { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
    }
}
