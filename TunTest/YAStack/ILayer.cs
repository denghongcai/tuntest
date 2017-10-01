namespace Tun2Any.YAStack
{
    interface ILayer
    {
        void In(byte[] frameBytes);

        byte[] Out();
    }
}
