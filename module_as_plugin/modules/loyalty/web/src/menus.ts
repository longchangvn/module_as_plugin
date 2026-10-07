export const menus = [
  {
    id: 'loyalty.members',
    parentId: 'loyalty',
    title: 'Members',
    route: '/loyalty/members',
    requiredPermission: 'LOYALTY_MEMBER_VIEW',
    order: 1,
  },
];

export const uiPermissions = ['LOYALTY_MEMBER_VIEW', 'LOYALTY_MEMBER_UPDATE'];
