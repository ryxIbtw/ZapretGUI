// Права на папку zapret. Из неё служба zapret запускается от имени SYSTEM, а задачи планировщика —
// с наивысшими правами, поэтому менять файлы в ней должны только администраторы (как в Program Files).
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ZapretGui
{
    static class FolderAcl
    {
        static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        const string CreatorOwner = "S-1-3-0";

        // запись, удаление, смена прав и владельца; плюс общие GENERIC_ALL / GENERIC_WRITE
        const int WriteMask = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)
            | 0x10000000 | 0x40000000;

        static bool Trusted(SecurityIdentifier s)
        {
            return s.Equals(Admins) || s.Equals(SystemSid) || s.Value == TrustedInstaller || s.Value == CreatorOwner;
        }

        static string Name(SecurityIdentifier s)
        {
            try { return s.Translate(typeof(NTAccount)).Value; } catch { return s.Value; }
        }

        // Кто, кроме администраторов и SYSTEM, может менять объект с такими правами; null — никто.
        public static string OthersWho(FileSystemSecurity sec)
        {
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner != null && !Trusted(owner)) return Name(owner) + L.T(" (владелец)", " (owner)");
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow) continue;
                var s = r.IdentityReference as SecurityIdentifier;
                if (s == null || Trusted(s)) continue;
                if (((int)r.FileSystemRights & WriteMask) != 0) return Name(s);
            }
            return null;
        }

        // Проверяет папку zapret и то, что из неё запускается с повышенными правами.
        public static string WritableBy(string root)
        {
            string exe = null;
            try { exe = System.Reflection.Assembly.GetEntryAssembly().Location; } catch { }
            var paths = new[] { root, Path.Combine(root, "bin"), Path.Combine(root, @"bin\winws.exe"), Path.Combine(root, "service.bat"), exe };
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p == exe && !(Path.GetDirectoryName(p) ?? "").Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    FileSystemSecurity sec;
                    if (Directory.Exists(p)) sec = Directory.GetAccessControl(p);
                    else if (File.Exists(p)) sec = File.GetAccessControl(p);
                    else continue;
                    string who = OthersWho(sec);
                    if (who != null) return who;
                }
                catch { }
            }
            return null;
        }

        static void ClearExplicit(FileSystemSecurity sec)
        {
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                sec.RemoveAccessRuleSpecific(r);
        }

        // Права корня: без наследования от диска, владелец — «Администраторы»,
        // администраторы и SYSTEM — полный доступ, пользователи — чтение и запуск.
        public static void HardenRoot(DirectorySecurity sec)
        {
            sec.SetAccessRuleProtection(true, false);
            ClearExplicit(sec);
            sec.SetOwner(Admins);
            var inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inh, PropagationFlags.None, AccessControlType.Allow));
        }

        // Вложенный объект со своими правами или чужим владельцем — к правам папки.
        public static void ResetChild(FileSystemSecurity sec)
        {
            sec.SetAccessRuleProtection(false, true);
            ClearExplicit(sec);
            sec.SetOwner(Admins);
        }

        // Нужны права администратора.
        public static void Harden(string root)
        {
            var sec = Directory.GetAccessControl(root);
            HardenRoot(sec);
            Directory.SetAccessControl(root, sec);   // унаследованные права расходятся по вложенным файлам сами
            foreach (string p in Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        var d = Directory.GetAccessControl(p);
                        if (OthersWho(d) == null && !d.AreAccessRulesProtected) continue;
                        ResetChild(d); Directory.SetAccessControl(p, d);
                    }
                    else
                    {
                        var f = File.GetAccessControl(p);
                        if (OthersWho(f) == null && !f.AreAccessRulesProtected) continue;
                        ResetChild(f); File.SetAccessControl(p, f);
                    }
                }
                catch { }
            }
            string left = WritableBy(root);
            if (left != null) throw new Exception(L.T("Права изменены не до конца, запись ещё есть у: ", "Permissions were only partly changed; write access remains for: ") + left);
        }
    }
}
