import json,sys
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=Path(sys.argv[1]);r=json.loads(p.read_text(encoding='utf-8-sig'));out=Path(sys.argv[2]);assert not out.exists()
rows=r['rounds'];x=list(range(1,len(rows)+1));m=1048576
fig,axes=plt.subplots(3,1,figsize=(10,8),sharex=True,gridspec_kw={'height_ratios':[3,1,1]},layout='constrained')
for key,label,color in [('PrivateBytes','Process private memory','#2166ac'),('LastGcCommittedBytes','Last GC committed snapshot','#b35806'),('ManagedBytes','Managed heap estimate','#1b7837')]:
 axes[0].plot(x,[v['afterClose'][key]/m for v in rows],marker='o',markersize=3,label=label,color=color,lw=1.7)
axes[0].set_ylabel('MiB');axes[0].legend(loc='lower left',fontsize=9);axes[0].set_ylim(bottom=0)
axes[1].plot(x,[v['afterClose']['Handles'] for v in rows],color='#762a83',marker='o',markersize=3);axes[1].set_ylabel('Handles');axes[1].set_ylim(bottom=0)
axes[2].step(x,[v['afterClose']['Gen2Collections'] for v in rows],where='post',color='#333333');axes[2].set_ylabel('Gen2 GCs');axes[2].set_xlabel('Round (100 edits, undo all, redo all, save/reopen, close)')
for ax in axes:
 ax.grid(axis='y',alpha=.2);ax.spines[['top','right']].set_visible(False);ax.set_xlim(.5,len(rows)+.5)
 for boundary in [9.5,18.5]: ax.axvline(boundary,color='#aaaaaa',ls='--',lw=.8)
fig.suptitle('Windows S05: natural post-close observations in one process',fontsize=14)
fig.text(.5,-.065,'No forced GC between rounds. Different memory APIs are not simultaneous allocation accounting.\nDiagnostic observations; resource acceptance remains open.',ha='center',fontsize=9,color='#555555')
fig.savefig(out,dpi=150,bbox_inches='tight');plt.close(fig);print(out)
