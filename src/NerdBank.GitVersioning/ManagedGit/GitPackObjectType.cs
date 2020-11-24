#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal enum GitPackObjectType
    {
        Invalid = 0,
        OBJ_COMMIT = 1,
        OBJ_TREE = 2,
        OBJ_BLOB = 3,
        OBJ_TAG = 4,
        OBJ_OFS_DELTA = 6,
        OBJ_REF_DELTA = 7,
    }
}
