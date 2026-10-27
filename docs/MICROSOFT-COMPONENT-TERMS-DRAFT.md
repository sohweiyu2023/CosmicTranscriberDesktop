# Microsoft component terms — publisher review draft

**Not effective terms. No agreement has been accepted or presented to recipients.**

Scope: the unmodified Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll from
Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 bundled with the Windows binary.
This draft does not restrict Soh Wei Yu's original MIT-licensed source.

The authoritative linked SDK terms are preserved at
[licenses/Microsoft-Windows-SDK-original.rtf](../licenses/Microsoft-Windows-SDK-original.rtf).
The separate Microsoft repository text mirror is also included for review;
its equivalence to every provision of the RTF is not presumed.

## Proposed recipient agreement structure

The publisher and recipient must be identified. The final agreement must apply
the applicable protections in the accompanying full SDK terms to these specific
Microsoft components, including platform/use/copy/redistribution restrictions,
notice preservation, intellectual-property protection, applicable statutory
exceptions, export obligations, and the applicable warranty/remedy/liability
protections. It must require distributors to obtain equivalent protection from
their recipients. A notice or link alone does not establish agreement.

The publisher must review which SDK provisions apply to redistributed components
and draft the complete recipient language accordingly; this outline is not an
automatic incorporation of SDK developer rights or a new Microsoft contract.
Publisher indemnity obligations must be reviewed separately, not silently shifted
to recipients by this draft.

## Decisions required before binary distribution

1. Approve the complete component-specific agreement and identify its parties.
2. Choose an affirmative agreement route before binary installation/use, with
   an appropriate agreement record. No installer or consent UI is implemented.
3. Confirm the program adds substantial primary functionality, remains Windows
   only, preserves Microsoft notices and ships the SDK files unmodified.
4. Review the publisher's SDK indemnity and other distribution obligations.
5. Resolve the restored application DLL's source provenance and the independent
   icon/binary-asset provenance issues. Restoring/excluding a file is not proof.
6. Audit the final framework-dependent or self-contained runtime payload and its
   additional .NET/component notices; this source draft is not binary clearance.

The draft publish entry point is deliberately held before any cleanup, SDK call
or build. A reviewed release change must resolve these items before lifting it.

Sources: https://aka.ms/WinSDKLicenseURL and
https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref.
