// A2GNet: network play. See net.c.
#pragma once

// Called from DllMain on process attach. Does nothing unless a2gnet.ini next to the exe enables it;
// then checks the build, installs the hooks (plain memory writes) and starts the network thread.
void NetEarlyInit(void);
